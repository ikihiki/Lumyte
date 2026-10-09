# 送信経路の直接エンコード比較

測定日: 2026-10-09。DiagnosticValue の wire DTO への再変換を省き、生成 Operation の scalar snapshot と JSON / MessagePack の直接 Writer を追加した。受信の復号・検証・Pump は維持する。

## 送信の比較

実際の protocolVersion = 1 の publication を使う。1 / 128 件の Metric イベントに、string / bool / long / double の 4 フィールドを付ける。long は 9007199254740993。setup で旧経路と直接経路の出力バイト一致を検証してから測定する。

| 経路 | 1 件 Mean | 1 件割り当て | 128 件 Mean | 128 件 StdDev | 128 件割り当て |
| --- | ---: | ---: | ---: | ---: | ---: |
| JSON source generation | 1.704 µs | 1,400 B | 184.96 µs | 7.06 µs | 116,864 B |
| JSON direct Writer | 1.479 µs | 928 B | 170.87 µs | 12.00 µs | 78,800 B |
| MessagePack DTO mapping | 0.862 µs | 1,456 B | 90.67 µs | 2.92 µs | 130,872 B |
| MessagePack direct Formatter | 0.403 µs | 528 B | 38.75 µs | 1.06 µs | 29,360 B |

128 件では MessagePack の CPU 時間が約 57%、割り当てが約 78% 減った。JSON の割り当ては約 33% 減ったが、時間差は分散と重なるため、速度の改善を確定する測定ではない。シリアライズ後の wire サイズは変わらない。

旧 MessagePack 経路は WireMapper.ToWire によるイベント・値・辞書の構築を含む。直接経路は DiagnosticMessageFormatter が収集済みの scalar を直接書く。JSON の旧経路は JsonSerializer.SerializeToUtf8Bytes と source-generated context、直接経路は再利用 ArrayBufferWriter と Utf8JsonWriter を使う。

全方式で最終出力を所有 byte[] に揃え、直接 JSON も最終コピーを含める。再利用 buffer の初回確保とイベント収集は測定外。実 HTTP 送信は Utf8JsonWriter から HttpContent のストリームへ書くため、この所有 byte[] は作らない。この測定は HTTP ストリーム全体の割り当て量や送信速度を表さない。動的フィールドの収集キューには DiagnosticValue と辞書を残しており、完全なゼロアロケーションではない。

## 生成 Operation

同じ Echo fixture で、生成出力の dictionary をなくした状態を測定した。

| 呼び出し | Mean | StdDev | 割り当て |
| --- | ---: | ---: | ---: |
| 通常の型付きメソッド | 10.52 ns | 0.27 ns | 72 B |
| 生成配送と入力・出力検証 | 66.36 ns | 1.48 ns | 144 B |

生成結果は scalar snapshot を保持し、validating Writer と物理 Writer が同じ WriteTo を呼ぶ。比較対象は通常のメソッド呼び出しであり、反射版ではない。結果の dictionary view を列挙する費用や Pump のキュー・ドメイン処理・通信は含まない。旧実装の測定は [元のレポート](README.md) に履歴として残す。

## 条件と再現

Linux x64 / Debian 13、AMD EPYC 9V74、.NET SDK 10.0.401 / Runtime 10.0.12、BenchmarkDotNet 0.15.8、MessagePack 3.1.11。ShortRun、LaunchCount = 1、WarmupCount = 3、IterationCount = 5、IterationTime = 150 ms。共有環境の短時間測定であり、production のスループット保証には使わない。

```sh
dotnet run --project benchmarks/Lumyte.Diagnostics.Benchmarks -c Release -- \
  --filter '*SendEncodingBenchmarks*' '*OperationBenchmarks*' \
  --artifacts artifacts/direct-encoding/benchmarks --exporters json
```

CSV と全 measurement を含む JSON は [生データ](direct-encoding-results/) に保存する。[測定ソース一覧](direct-encoding-results/source-hashes.json) で内容を特定する。

## 機能確認

- 旧 JSON / MessagePack と byte 一致、復号後の一致。
- 4 種の scalar、特殊文字、Int64 上下限、空 batch、成功・拒否・競合・Heartbeat。
- generated-style snapshot の検証と送信が dictionary view を materialize しないこと。
- duplicate name、null string、NaN の結果を validating Writer が拒否すること。
- 実 Generator をコンパイル・実行し、ドメインの戻り値を後から変更しても送信 snapshot の値が変わらないこと。
- 実 TCP と独立プロセスの両方式で Input 操作・標準 Telemetry・切断時の解除が継続すること。

詳細は [実通信検証](../../diagnostics/communication-verification.md) と [今回の別プロセス結果](../../diagnostics/results/direct-encoding-processes.json) を参照する。大容量送信中の制御応答時間、受信側のベンチマーク、Browser/WASM、NativeAOT は今回の測定対象外。

この測定後の変更・前後比較は [全体レビュー後のクリーンアップ](cleanup.md) に記録する。
