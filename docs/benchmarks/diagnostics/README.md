# 診断実装とシリアライズの性能比較

測定日: 2026-10-09。ゲーム側の DI / Operation Generator / 標準 Metrics・Trace・Log の収集を実装し、ローカル実行とテストに加えて BenchmarkDotNet で性能を比較した。

この条件では MemoryPack の CPU 時間が最も短く、protobuf-net の構造化データのサイズが最小だった。MagicOnion 向け MessagePack と Browser 向け JSON を置き換える根拠にはせず、CPU・帯域・互換性・配布先を分けて判断する。以下はネットワーク往復性能の測定ではない。後から追加した通信アダプターと診断サーバーの確認結果は [実通信検証](../../diagnostics/communication-verification.md) に記録する。

## 128 件の Metrics バッチ

表は **2 回目の実行の Mean ± StdDev**。符号化は独立した所有 byte[] の生成、復号は同一内容の型付きオブジェクト構築を含む。1 件あたりの時間ではなく 128 件全体の時間である。

| ライブラリ | 符号化 µs | 復号 µs | 出力 byte | 割り当て byte（符号化 / 復号） |
| --- | ---: | ---: | ---: | ---: |
| System.Text.Json | 132.25 ± 15.86 | 266.64 ± 34.02 | 56,326 | 71656 B / 125664 B |
| MessagePack | 43.85 ± 0.59 | 65.09 ± 1.46 | 15,285 | 15312 B / 87232 B |
| MemoryPack | 10.55 ± 0.06 | 24.01 ± 1.62 | 24,765 | 24792 B / 87208 B |
| protobuf-net | 55.23 ± 2.89 | 73.35 ± 2.05 | 12,977 | 46776 B / 68864 B |

MemoryPack は MessagePack より符号化が約 4.2 倍、復号が約 2.7 倍速かった。一方、サイズは MessagePack の約 1.62 倍。protobuf-net は MessagePack より約 15% 小さい。MessagePack / protobuf-net の小さな速度差を一般的な順位として扱わない。

JSON は Browser で安全に扱うため全 Int64 を十進文字列にしている。標準の Unicode escaping を使用し、圧縮は行っていない。数値として出力する JSON や、別のエスケープ設定とは条件が異なる。

## その他のサイズ

| データ | JSON | MessagePack | MemoryPack | protobuf-net |
| --- | ---: | ---: | ---: | ---: |
| Operation | 470 | 173 | 249 | 161 |
| Metrics | 56,326 | 15,285 | 24,765 | 12,977 |
| Trace | 64,006 | 23,605 | 34,237 | 22,193 |
| Log | 70,011 | 27,463 | 38,223 | 24,643 |
| Graph | 438,007 | 117,053 | 191,061 | 99,049 |
| Image | 349,634 | 262,198 | 262,205 | 262,195 |

Operation は 1 件の scalar 結果、Metrics / Trace / Log は各 128 件、Graph は 1,000 件のフラットな合成ノード、Image は固定 seed の 256 KiB バイナリ。Graph の実エンジン走査・循環参照処理や GPU 読み戻しは含まない。すべて明示的な detached DTO を使う。

Image の JSON は base64 により約 33% 大きくなる。2 回目では画像の JSON 符号化は MessagePack より速いケースもあったため、「バイナリなら常に高速」とは結論しない。MessagePack と protobuf-net の byte[] API はこのサイズで中間バッファ／コピーの割り当ても見える。画像は ADR の方針どおり制御メッセージと分け、バイナリ転送にする。

## 診断処理そのもの

| 測定 | Mean | StdDev | 割り当て / 呼び出し |
| --- | ---: | ---: | ---: |
| 通常の型付きメソッド | 10.41 ns | 0.60 ns | 72 B |
| 生成 Operation + スキーマ・権限検証 + 結果変換 | 79.88 ns | 1.19 ns | 400 B |
| 標準 Counter.Add + drain（診断無効） | 8.71 ns | 0.10 ns | 0 B |
| 標準 Counter.Add + scalar コピー + queue + drain（診断有効） | 151.72 ns | 1.78 ns | 496 B |

Operation の追加分は約 69 ns / 328 B。これは空に近い Echo ハンドラーの `DiagnosticOperationSet.Invoke` であり、Pump のキュー・重複排除・Input ドメイン処理・通信は含まない。生成コードによる高速化を示す反射版との比較ではなく、実装した検証・結果変換の費用を確認するベンチマークである。

Metric は queue を毎回 drain し、満杯による破棄が高速経路として測定されるのを防ぐ。drain の費用を含み、実際の送信・集約は含まない。当時の実装は測定イベントごとに辞書を作るため、有効時 496 B の割り当ては高頻度記録では問題になる。毎秒 10 万件ならこの経路だけで約 49.6 MB/秒の managed 割り当てとなる計算で、次段階では bounded aggregation と buffer pooling を検討する。この数字はそのレートでの負荷試験結果ではない。

Trace と Log は実際の標準 API で収集・相関を検証したが、収集コストのベンチマークは Counter に限定する。Trace / Log のシリアライズ性能は全測定に含める。

上記は直接エンコード導入前の履歴である。生成 Operation の現在の結果表現と送信経路は [直接エンコードの測定](direct-encoding.md) を参照する。

## 測定条件

- Debian 13 x64、AMD EPYC 9V74、.NET SDK 10.0.401 / Runtime 10.0.12、Release / RyuJIT。コンテナの CPU quota は 4 CPU 相当で、BenchmarkDotNet が報告する 5 logical cores と異なる。
- BenchmarkDotNet 0.15.8、1 launch、3 warmup、5 iteration、目標 150 ms / iteration、MemoryDiagnoser。
- System.Text.Json は .NET 10.0.12 の source-generated context + 明示的な Int64 converter。
- MessagePack 3.1.11 は numeric-key schema + generated resolver（dynamic reflection fallback を DTO に使わない）。MemoryPack 1.21.4 は generated formatter。protobuf-net 3.4.30 は明示 field-number schema。
- 同じ class / array / scalar-union の DTO を各ライブラリ用の属性で共有し、準備時に全フィールドの往復一致を確認する。Int64 > 2^53、日本語文字列、bool、double、null ID、バイナリを含める。
- protobuf-net の初回 schema 作成・JIT は Setup で除外する。符号化には MemoryStream + ToArray、復号には読み取り専用 MemoryStream を使う。他のライブラリも公開 byte[] API の費用を含む。共有 IBufferWriter に対するゼロコピー条件とは異なる。
- 圧縮、転送、TLS、HTTP、gRPC / MagicOnion framing、エンジン走査、GPU、NativeAOT、Browser / JavaScript 実行は対象外。JSON の整数規約の往復確認は .NET 側で行った。

最初の 48 ケースに加え、Operation / Metric を追加した同じ 48 ケース + 4 ケースを再測定した。確認実行は 52 ケースすべて完了し、初回の結果も残す。低い値だけを抜き出した集計は行わない。

共有実行環境で High Priority は許可されず、一部 iteration は 100 ms 未満だった。JSON Log 復号などは StdDev が Mean の 20% を超える。BenchmarkDotNet 既定の outlier 処理と警告を生ログに残した。短時間の探索的測定なので、専用機・実運用バッチ・高頻度送信で受け入れ基準を定める前に再検証する。

## 実装と検証

[ゲーム側 API](../../../src/Diagnostics/Lumyte.Diagnostics/README.md)、[Generator](../../../src/Diagnostics/Lumyte.Diagnostics.Generators/README.md)、[Input サンプル](../../../samples/Lumyte.Diagnostics.Sample/README.md) を追加した。

Release ビルドで warnings / errors は 0。17 件のテストで生成コードの実コンパイル、不正宣言、DI スコープ共有と分離、所有スレッド、引数・権限、重複実行、期限・キャンセル、Input リース期限、Meter の所属、Log / Trace の相関、外部 Listener とのサンプリング共存、キュー欠落、収集停止を確認した。CI にはサンプル実行・全シリアライザーの往復一致を追加し、時間の閾値は設けない。

MagicOnion / HTTP の実接続、サーバー、トークン認証は別途実装・検証した。実入力・描画統合、登録世代、集約・購読、秘匿化、AOT / Browser 実行は後続実装／検証として ADR に明示する。

## 再現

リポジトリ指定の .NET SDK を使い、ルートから実行する。NuGet.config のローカルフィードのディレクトリも準備する。

```sh
mkdir -p artifacts/nuget
dotnet test tests/Diagnostics/Lumyte.Diagnostics.Tests -c Release
dotnet run --project samples/Lumyte.Diagnostics.Sample -c Release
dotnet run --project benchmarks/Lumyte.Diagnostics.Benchmarks -c Release -- --verify
dotnet run --project benchmarks/Lumyte.Diagnostics.Benchmarks -c Release -- --filter '*' --artifacts artifacts/diagnostics-benchmarks
```

## 生データ

- [全シリアライズ結果・確認実行](confirmation/Lumyte.Diagnostics.Benchmarks.SerializationBenchmarks-report-github.md) / [CSV](confirmation/Lumyte.Diagnostics.Benchmarks.SerializationBenchmarks-report.csv)
- [Operation 結果](confirmation/Lumyte.Diagnostics.Benchmarks.OperationBenchmarks-report-github.md) / [CSV](confirmation/Lumyte.Diagnostics.Benchmarks.OperationBenchmarks-report.csv)
- [Metric 収集結果](confirmation/Lumyte.Diagnostics.Benchmarks.InstrumentationBenchmarks-report-github.md) / [CSV](confirmation/Lumyte.Diagnostics.Benchmarks.InstrumentationBenchmarks-report.csv)
- [確認実行の生ログ・警告（gzip）](confirmation/run.log.gz)
- [初回シリアライズ結果](initial/Lumyte.Diagnostics.Benchmarks.SerializationBenchmarks-report-github.md) / [CSV](initial/Lumyte.Diagnostics.Benchmarks.SerializationBenchmarks-report.csv) / [生ログ（gzip）](initial/run.log.gz)
- [ペイロードサイズ](payload-sizes.jsonl)
- [測定ソースの SHA-256](source-sha256.json)

最新の全体レビューと前後比較は [クリーンアップ後の測定](cleanup.md) を参照する。
