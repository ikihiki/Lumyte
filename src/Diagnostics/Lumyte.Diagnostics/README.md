# Lumyte.Diagnostics

DI スコープごとに Operation の公開・安全な実行と、標準 .NET Metrics / Trace / Log の有界収集を提供する。サブシステムには通信ライブラリの型・属性を持ち込まない。

## Operation の公開

```csharp
public sealed partial class InputDiagnostics(InputOverrideService input)
{
    [DiagnosticOperation(DiagnosticPermission.OverrideInput)]
    private DiagnosticResult<ButtonOverrideReceipt> OverrideButton(
        DiagnosticOperationContext context, string button, bool pressed, long durationMs)
        => input.Override(context, button, pressed, durationMs);
}

public sealed record ButtonOverrideReceipt(string LeaseId);
```

`Lumyte.Diagnostics.Generators` を Analyzer として参照する。型、引数、結果には属性を必要としない。Generator が `IDiagnosticContributor`、スキーマ、private メソッドへ直接配送するコードを生成する。詳細は [Generator](../Lumyte.Diagnostics.Generators/README.md) を参照する。

```csharp
services.AddScoped<InputOverrideService>();
services.AddDiagnosticExecutionPoint<BeforeInputProcessing>("input.before-processing");
services.AddDiagnosticSubsystem<InputDiagnostics, BeforeInputProcessing>(
    new("input", "Input", 1));
```

`TContributor` は Scoped に限定し、既に Scoped 登録されている場合はそのインスタンスを共有する。ポイント ID、サブシステム ID、Contributor の二重登録は拒否する。各ゲーム実行に明示的な `IServiceScope` を作り、そのスコープから Pump とドメインサービスを解決する。

生成結果は `DiagnosticOutputValues` に scalar をコピーし、型別の `IDiagnosticValueWriter.Write` で直接出力する。`DiagnosticValueWriting.WriteTo(values, ref writer)` は snapshot を dictionary に変換せず扱う。既存の `result.Values` の indexer や列挙は互換用で、その時に dictionary を生成する。サブシステムには通信の型・属性を追加しない。動的な計測値とタグには収集キューの scalar として DiagnosticValue を引き続き使う。

## 実行契約

- `IDiagnosticPump<TPoint>.Activate()` が呼び出しスレッドを所有者として固定し、全スキーマを一括構築する。失敗時は部分公開しない。同じスレッドでの再呼び出しは冪等。
- `Catalog` はアクティブなサブシステムの記述子と Operation をコピーして返す。通信側のカタログ公開に使える。非アクティブ時は空。
- 信頼済みホストが `SubmitAsync(DiagnosticRequest)` に投入する。DTO 内の権限は認証済みホストから与え、外部要求の自己申告をコピーしない。このライブラリ自体は認証しない。
- 所有スレッド上の入力処理前などで `Pump(frame, budget)` を呼ぶ。期限、権限、キャンセル、期待リビジョンの有無、引数スキーマを実行前に検証する。実際のリビジョン比較はドメイン側の責務。
- キュー上限は 256 件、重複排除キャッシュは 1,024 件。同一 SessionId / RequestId と同一内容は元の Task を返し、異なる内容は `request-id-conflict`。完了結果は期限まで保持する。ネットワーク全体の exactly-once は保証しない。
- 期限は注入した `TimeProvider.GetTimestamp()` の絶対時刻。フレームの Timestamp と取り違えない。予算はコマンド間で確認し、長い同期ハンドラーを中断しない。
- `Deactivate()` は未実行要求を `target-gone` で完了し、カタログを解放する。異なる所有スレッド、再入 Pump、ハンドラー中の解除は拒否する。
- `DiagnosticOperationSet` は単独で生成コードを直接呼び出すための型。所有スレッドと期限の管理は Pump が担当する。成功出力も検証するが、出力エラーや例外による既存の副作用をロールバックしない。

`DiagnosticValue` は bool / Int64 / double / string の discriminated scalar。NaN / Infinity を拒否し、文字列は既定で 4,096 UTF-16 コード単位。明示的な整数上下限は double 属性で正確に表せる ±(2^53−1) に限定するが、上下限を指定しない Int64 値は全範囲を扱える。

## 標準テレメトリー

```csharp
services.AddLumyteDiagnostics(options =>
{
    options.Enabled = true; // 既定は無効。
    options.AllowedMeterNames = ["Lumyte.Input"];
    options.AllowedActivitySourceNames = ["Lumyte.Input"];
    options.AllowedLogCategoryPrefixes = ["Lumyte.Input"];
    options.TraceSampleRatio = 0.1;
});
```

スコープから `DiagnosticTelemetry` を解決して `Start()` を呼ぶ。`IMeterFactory` で Meter を作り、Metric の Record / Add と Activity の開始時タグに `lumyte.instance.id` を付ける。Log は同じキーを `ILogger.BeginScope` に入れる。値はそのスコープの `IGameExecutionIdentity.InstanceId.ToString("D")`。タグのないデータや別スコープのデータは収集しない。

`MeterListener` は登録元 MeterFactory の Scope と Meter 名を照合する。byte / short / int / long / float / double の同期 Instrument を収集し、Observable と decimal は初期実装の対象外。Metric は集約前の測定イベントであり、Histogram バケットや購読管理は未実装。

`ActivityListener` は完了した Activity の名前、ID、親 Span ID、期間、Status、タグを収集する。root は TraceId による決定的サンプリング、子は親の Recorded フラグに従う。外部 Listener が root を記録しても、自分の収集判定を再確認する。instance タグは開始時に与える。Activity の Events / Links は初期実装の対象外。

Singleton の `ILoggerProvider` は Scoped サービスを捕捉せず、instance スコープから生存中の収集器へルーティングする。構造化状態、OriginalFormat、LogLevel、EventId、現在の Trace / Span ID、例外型とメッセージをコピーする。ログカテゴリとレベルを制限し、転送エラーの再帰ログを避ける。

コールバックは scalar のコピーと `TryWrite` までで、シリアライズや通信を行わない。既定容量 1,024 のキューが満杯なら待たずに破棄し、`Dropped` を増やす。`TryRead` で取り出した `DiagnosticEvent` をバックグラウンド送信に渡せる。タグは先頭 32 個、キー 128 文字、文字列 4,096 文字に制限し、未知のオブジェクトを保持しない。タグ値の秘匿化・監査方針は送信側で追加する。設定は起動前に確定する。

## 実装範囲と検証

[実行可能な Input サンプル](../../../samples/Lumyte.Diagnostics.Sample/README.md)、[性能比較と生データ](../../../docs/benchmarks/diagnostics/README.md) を参照する。

このパッケージはゲーム側の実行・収集の基盤である。[診断サーバー](../Lumyte.Diagnostics.Server/README.md) と [共通通信エージェント](../Lumyte.Diagnostics.Transport/README.md) を追加し、MagicOnion / HTTP の実接続を検証した。[検証結果](../../../docs/diagnostics/communication-verification.md) を参照する。登録世代、実エンジンのグラフ走査、描画キャプチャー、大容量転送は後続実装範囲。本パッケージのベンチマークをネットワーク往復性能と読み替えない。
