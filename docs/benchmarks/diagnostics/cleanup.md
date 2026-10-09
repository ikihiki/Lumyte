# 全体レビュー後のクリーンアップと性能比較

測定日: 2026-10-09。直前の直接エンコード実装（0c7a1c1）を別ディレクトリに保存して改善前を測り、同じ条件で改善後を測定した。比較はネットワーク往復時間ではなく、収集・符号化・復号・サーバー保持それぞれの処理時間と managed 割り当てを対象とする。

## 変更

- publication の WireMessage / WireEvent / WireResult と本番の変換経路を除去。旧実装はベンチマーク用 fixture に移動した。
- MessagePack は共通モデルへ直接復号する。確保前の件数上限、重複キー拒否、未知末尾も含む depth 制限を検証する。送信 GUID も出力バッファへ直接書く。
- Metric のタグ配列化と LINQ、Log の一時 List・ID 配列・連結を除去。Dictionary の具体型列挙で boxing を避け、JSON の固定プロパティ名を事前エンコードする。
- サーバーの所有権確保を型付きコピーにし、JSON の再解析を除去。テレメトリーとカタログの外向きコピーを共有 lock の外へ移す。canonical JSON のサイズ制限・指紋算出は維持する。
- Agent のアイドル時の List 確保を除去し、件数と byte budget で batch を分割する。

## 前後比較

数値は Mean。128 件の fixture は前回と同じ 4 scalar fields を持つイベントである。送信の最終出力はすべて所有 byte[] とし、JSON の直接経路は再利用 buffer からの最終コピーも含める。

| 処理 | 改善前時間 | 改善後時間 | 改善前割り当て | 改善後割り当て |
| --- | ---: | ---: | ---: | ---: |
| Metric の記録・drain（1 件） | 159.48 ns | 148.20 ns | 496 B | 400 B |
| JSON 直接符号化（128 件） | 171.39 µs | 112.44 µs | 78,800 B | 68,560 B |
| MessagePack 直接符号化（128 件） | 35.48 µs | 35.72 µs | 29,360 B | 18,928 B |
| サーバー Publish（128 件） | 2,572.00 µs | 593.99 µs | 582,042 B | 219,724 B |
| サーバー保持値の取得（128 件） | 2,647.07 µs | 11.46 µs | 467,608 B | 79,976 B |

MessagePack の送信時間は今回の変更ではほぼ同じで、割り当てが約 35.5% 減った。この符号化ベンチマークの 18,928 B は最終 byte[] の確保に相当する。収集やネットワークを含む全体のゼロアロケーションを意味しない。Metric の割り当ては約 19.4%、JSON の送信割り当ては約 13.0% 減った。

サーバーは JSON の全体を再解析してコピーする費用を削減し、Publish の割り当てが約 62.2%、保持値の取得が約 82.9% 減った。Publish は検証・ソートコピー・canonical JSON・SHA256・保持・容量制御を含み、受信デコードは含まない。ReadRetained は取得結果の可変コンテナーをすべてコピーし、元の値を変更できない契約を維持する。

サーバーの改善後 Publish は初回 537.43 µs（StdDev 389.19 µs）とばらつきが大きかったため、単独で確認実行した。表は確認実行の 593.99 µs（StdDev 191.81 µs）を使用する。改善前は StdDev 111.95 µs。共有環境の短時間測定であり、具体的な速度比や production のスループット保証には使わない。初回・確認実行とも生データを残した。

JSON 直接符号化の StdDev は前 20.93 µs、後 5.51 µs。MessagePack は前 0.20 µs、後 1.43 µs。生成 Operation は前 64.13 ns / 144 B、後 64.87 ns / 144 B で、今回の変更による高速化は主張しない。診断収集を無効にした Metric は両実行とも割り当て 0 B。

## サーバー側の MessagePack 復号

改善後の同一実行内で、benchmark 専用の旧 DTO 経路と直接復号を比較した。両方とも同じ入力を復号して共通モデルを返す。

| 128 件の復号 | Mean | StdDev | 割り当て |
| --- | ---: | ---: | ---: |
| 旧 DTO 復号＋変換 | 111.45 µs | 3.32 µs | 224,696 B |
| 直接復号 | 63.75 µs | 0.91 µs | 116,016 B |

この条件では処理時間が約 42.8%、割り当てが約 48.4% 減った。これは大量の publication を受信するサーバー側の測定であり、少量の操作要求を受けるゲーム側の測定ではない。

## 確認した不具合の修正

- 1,024 件の重複キャッシュが Dictionary の空き slot 再利用によって新しい要素を削除し続ける問題を、明示 FIFO で修正。
- 返却した権限・catalog・telemetry・command・result の可変コンテナーを通じた内部状態の変更を防止。
- 合法な大きいタグでも 128 件を一括にすると 4 MiB を超える問題を、保守的な byte budget と次 batch への保持で修正。
- Generator の内部型名を予約済み __Lumyte prefix へ揃え、利用者の型名との衝突を回避。

基盤・Generator 23 件、統合 31 件が成功した。旧 formatter から採取した固定 wire データ、不正な配列・重複キー・depth、エスケープ文字を含む巨大タグの batch 分割、FIFO、snapshot 分離を検証している。別プロセスの HTTP / MagicOnion でも Input 操作・標準 Telemetry・切断時解除を再確認した。[通信結果](../../diagnostics/results/cleanup-processes.json) を参照する。

## 条件と再現

Linux x64 / Debian 13、AMD EPYC 9V74、.NET SDK 10.0.401 / Runtime 10.0.12、BenchmarkDotNet 0.15.8、MessagePack 3.1.11。ShortRun: LaunchCount 1、WarmupCount 3、IterationCount 5、IterationTime 150 ms。

改善前は 0c7a1c1 に今回の ServerRegistryBenchmarks だけを加えた 16 ケース、改善後は 23d2462 の実装で ReceiveDecodingBenchmarks を加えた 20 ケース、追加確認はサーバー 4 ケースを実行した。送信 benchmark の条件を変えず、旧 DTO の比較 fixture は本番から benchmarks へ移動した。

```sh
dotnet run --project benchmarks/Lumyte.Diagnostics.Benchmarks -c Release -- \
  --filter '*SendEncodingBenchmarks*' '*ReceiveDecodingBenchmarks*' \
    '*ServerRegistryBenchmarks*' '*InstrumentationBenchmarks*' '*OperationBenchmarks*' \
  --artifacts artifacts/diagnostics-cleanup/after --exporters json
```

測定後に main の f69072f を取り込み、新しい primary constructor 規約に合わせてコンストラクターの記法を変更した。測定対象の処理本体は変えていない。取り込み後も 54 件のテストと HTTP / MagicOnion の別プロセス通信を確認した。

[生データ](cleanup-results/) に before / after / server-confirmation の CSV と全 measurement JSON、ソースの hash を保存した。4 ライブラリの元の比較、今回の符号化と復号の同値性も確認している。ログ・Trace の収集時間、混雑時の制御遅延、Browser/WASM、NativeAOT の性能は今回の測定対象外。
