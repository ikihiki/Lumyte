# ADR-GRAPHICS-0002: 型付きバッファの所有・サイズ・明示的なCPUアクセス

- 状態: 置換済み（実行時検証の方針のみ。その他の決定は引き続き採用）
- 日付: 2026-10-08
- 変更日: 2026-10-10
- 置換範囲: deviceの子resource数による解放拒否。利用者が寿命と同期を管理し、検証のための追跡費用を省く方針へ変更する。
- 後継: [ADR-GRAPHICS-0014](GRAPHICS-0014-caller-managed-resource-validation.md)。以下の本文は判断時点の記録であり、上記の実行時検証には後継を適用する。

## 背景

[GRAPHICS-0001](GRAPHICS-0001-graphics-device.md) の生成済みデバイス契約を、buffer の確保と CPU memory access に拡張する。数値型・unmanaged struct を同じ API で扱い、要素数と byte 数を取り違えず、backend の GPU copy alignment を T の要素単位でも確認できるようにする。

Upload／Readback の staging 確保、GPU copy の記録・送信、同期と CPU copy を利用者が明示的に組み合わせる。CPU copy の名前の裏で GPU work や待機を行うと、処理の責務とコストが分からなくなる。

## 決定

### 配置と責務

公開 interface、Desc、layout、slice、usage、memory preference は `Lumyte.Graphics.Abstractions` に置く。具象 backend buffer は byte 範囲の検証と CPU copy も含めた `IGraphicsBuffer<T>` を直接実装し、native resource と CPU access の状態を一つの instance で所有する。BufferToken や所有 facade を設けない。

デバイス自体の生成方法は引き続き backend 固有とする。生成済みデバイスの buffer 確保と layout 問い合わせは `IGraphicDevice` の共通操作として追加する。backend 固有の生成条件、usage 制約、memory type、mapping の処理は各 backend README に記載する。

### API 差分

比較元は origin/main。既存の Caps 契約を保ち、次の API を追加する。

```diff
 namespace Lumyte.Graphics.Abstractions
 {
     public interface IGraphicDevice
     {
+        // allocation を作らず、T の raw layout と device の GPU copy 制約を返す。
+        // stride は sizeof(T)。byte alignment と要素単位の倍数を返し、size を丸めない。
+        // 解放済み device は ObjectDisposedException。
+        BufferLayout<T> GetBufferLayout<T>() where T : unmanaged;
+        // Desc の Count を保ったまま buffer を確保する。生成済み device の共通操作。
+        IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc) where T : unmanaged;
     }

+    public sealed record BufferDesc<T> where T : unmanaged
+    {
+        // Count > 0。byte 数は backend の Layout で checked 計算する。
+        public required ulong Count { get; init; }
+        // 0・未知 flags・backend が対応しない組み合わせは拒否する。
+        public required BufferUsage Usage { get; init; }
+        public MemoryPreference Memory { get; init; }
+    }

+    [Flags]
+    public enum BufferUsage
+    {
+        CopySource = 1,
+        CopyDestination = 2,
+        ShaderRead = 4,
+        ShaderWrite = 8,
+        Index = 16,
+    }

+    public enum MemoryPreference
+    {
+        // CPU mapping は提供しない。
+        Automatic,
+        // 明示的な mapping の完了後、CopyTo で CPU memory に読み出す。
+        Readback,
+        // 明示的な mapping の完了後、CopyFrom で CPU memory へ書き込む。
+        Upload,
+    }

+    public readonly struct BufferLayout<T> where T : unmanaged
+    {
+        // backend が数値を設定する。raw storage の stride は sizeof(T) と一致させる。
+        // 不正なサイズ・0 alignment は ArgumentOutOfRangeException。
+        public BufferLayout(ulong elementSize, ulong stride, ulong offsetAlignment, ulong sizeAlignment);
+        public ulong ElementSizeInBytes { get; }
+        public ulong ElementStrideInBytes { get; }
+        public ulong CopyOffsetAlignmentInBytes { get; }
+        public ulong CopySizeAlignmentInBytes { get; }
+        // alignment / gcd(alignment, stride)。要素数や size を丸めない。
+        public ulong CopyOffsetAlignmentInElements { get; }
+        public ulong CopyCountAlignment { get; }
+        // checked(count * stride)。overflow は OverflowException。
+        // default layout は InvalidOperationException。
+        public ulong GetSizeInBytes(ulong count);
+    }

+    // concrete backend が直接実装する、一つの所有 allocation。
+    // byte 範囲検証と CPU copy を同じ interface に含める。
+    // token・別所有 wrapper・command 記録・barrier 処理は持たない。
+    public interface IGraphicsBuffer<T> : IDisposable where T : unmanaged
+    {
+        public ulong SizeInBytes { get; }
+        // length > 0、offset <= SizeInBytes、length <= SizeInBytes - offset。
+        // disposed は ObjectDisposedException、範囲不正は ArgumentOutOfRangeException。
+        public void ValidateRange(ulong offset, ulong length);
+        // mapped Upload に source.Length byte をコピー。範囲の余りは変更しない。
+        // source.Length <= length。mapping・GPU work・待機・staging 確保を行わない。
+        public void CopyFrom(ReadOnlySpan<byte> source, ulong offset, ulong length);
+        // mapped Readback の length byte を destination にコピー。余りは変更しない。
+        // destination.Length >= length。mapping・GPU work・待機を行わない。
+        public void CopyTo(Span<byte> destination, ulong offset, ulong length);
+        public BufferLayout<T> Layout { get; }
+        public ulong Count { get; }
+        public BufferUsage Usage { get; }
+        public MemoryPreference Memory { get; }
+        public bool IsMapped { get; }
+        // Upload／Readback 全体を明示的に map する。Automatic は InvalidOperationException。
+        // native mapping の完了を待つ操作。GPU copy 命令は生成・送信しない。
+        // 二重 map／pending 中の再 map は InvalidOperationException。
+        // cancellation は native request 完了後に map を解放して通知する。
+        public ValueTask MapAsync(CancellationToken cancellationToken = default);
+        // mapping を解放し GPU access に戻す。GPU 命令は生成しない。
+        public void Unmap();
+        // count > 0 の非所有範囲。範囲不正は ArgumentOutOfRangeException。
+        public BufferSlice<T> Slice(ulong offset, ulong count);
+        // mapped Upload への CPU copy。source.Length <= Count。
+        public void CopyFrom(ReadOnlySpan<T> source);
+        // mapped Readback の Count 要素を CPU copy。destination.Length >= Count。
+        public void CopyTo(Span<T> destination);
+        // idempotent。pending mapping 中は InvalidOperationException。
+        // buffer を使う GPU work の完了は利用者が保証する。
+        public void Dispose();
+    }

+    // 非所有の値型。default は無効で、操作時に ArgumentException。
+    public readonly struct BufferSlice<T> where T : unmanaged
+    {
+        // 範囲を検証する。buffer の寿命を延ばさない。
+        public BufferSlice(IGraphicsBuffer<T> buffer, ulong offset, ulong count);
+        public IGraphicsBuffer<T> Buffer { get; }
+        public ulong Offset { get; }
+        public ulong Count { get; }
+        public ulong OffsetInBytes { get; }
+        public ulong SizeInBytes { get; }
+        // 範囲内で親 buffer と同じ mapped CPU copy 契約を適用する。
+        public void CopyFrom(ReadOnlySpan<T> source);
+        public void CopyTo(Span<T> destination);
+    }
 }

```

### 型とサイズ

T は unmanaged の数値型、enum、struct を扱う。raw storage の要素 stride は host の sizeof(T) で、Count と SizeInBytes を backend alignment のために丸めない。shader の ABI と一致するとは仮定せず、shader target layout の pack は別の責務とする。buffer 内容を定義する application の struct を Abstractions に置かない。

backend は GPU copy offset／length alignment を byte の数値で返す。それを stride との最大公約数で換算した CopyOffsetAlignmentInElements／CopyCountAlignment も返す。例えば byte alignment が 4 で stride が 1、2、4、12 の場合、要素単位の必要な倍数はそれぞれ 4、2、1、1 になる。GPU copy を記録する側は、これらと最終 byte 範囲を検証し、count を暗黙に補正しない。

範囲検証は subtraction を用いて overflow を避け、byte 換算は checked で行う。backend の native memory allocation に必要な内部 alignment と、利用者に公開する論理 buffer size は区別する。

### Mapping とCPU copy

状態は Unmapped → Pending → Mapped → Unmapped とする。Automatic memory は CPU mapping を提供しない。MapAsync は利用者が呼ぶ明示的な mapping 操作で、native API の都合により GPU access の完了を待つことがある。CopyFrom／CopyTo は既に map された CPU memory のコピーだけを行い、GPU 命令、queue write、map、submit、待機、staging allocation を行わない。

利用者は GPU copy と同期を command 側の API で行い、Upload を GPU に使わせる前に Unmap を呼ぶ。Readback も利用者が確保・GPU copy・送信を行った後に MapAsync と CopyTo を呼ぶ。buffer が barrier を適用する API は設けない。command、submit、barrier の設計は本 ADR の対象外とする。

CPU copy は mapped 状態と Memory の方向を検証する。不正な状態・アクセス方向は InvalidOperationException、span の長さ不正は ArgumentException。CopyFrom の空 source は有効な範囲への no-op とする。CopyTo は範囲全体を読み、destination の余りを変更しない。

### 所有と寿命

buffer は device より先に解放する。所有 buffer が残った device の Dispose は InvalidOperationException として拒否する。slice は buffer を所有せず、解放済み allocation への操作は失敗する。native request が callback を参照している間は mapping の取消・解放を完了扱いにせず、request 完了後に安全に cleanup する。

GPU 実行中の allocation の解放や CPU access の開始を避ける同期は利用者の責務とする。GPU address、native handle、map pointer は共通 API に公開しない。

## 検討した代替案

- byte 数だけの buffer: 数値型や struct の要素数と byte offset を利用側が毎回計算する。型付き Count と backend layout を共通 API にする。
- GPU copy と staging を自動化する CPU copy: 隠れた確保・送信・待機が発生する。mapping と CPU copy を明示的に分ける。
- alignment のために Count／SizeInBytes を補正する: 利用者の指定範囲と実際の論理範囲が変わる。必要な倍数を公開し、不適合な要求は拒否する。
- 共通の所有 Buffer class と内部 driver: allocation に対応する object が増え、所有が分散する。具象 backend が所有 interface を直接実装する。

## 結果と影響

- 共通 API で buffer を確保し、型に応じた正確な byte size と copy alignment を取得できる。
- slice は値型で、allocation を増やさず部分範囲を扱える。
- Upload／Readback は利用者が staging、mapping、GPU copy、同期を明示的に管理する。
- backend によって対応する usage／memory の組み合わせが異なり、README で生成条件を確認する必要がある。

## 検証方針

数値型と unmanaged struct の size／alignment、overflow と空範囲、CPU copy の方向と状態、slice の境界、解放済み access、device と buffer の寿命を共通 API で検証する。backend 固有の生成だけを起動部分で行う。実 GPU／browser の検証は既存 CI で行う。

## 別途決定する事項

- command buffer による GPU copy、barrier、送信と完了の契約。
- shader layout と serializer、GPU 内の不透明参照、binding。
- suballocation と pooling。
