# 保持描画の製品実装データ

測定sourceは `c217c08847db36802fc96f18445652f560a101af`。`manifest.json` の accepted だけを正式集計に使用します。checks は単体テスト・GPU画素・AOT等、rejected-disposal-hardening は旧sourceの比較であり集計しません。

公開コピーはhome/checkout/host/userを伏せています。`sha256` と `bytes` は展開後の公開コピー、`originalSha256` は伏せる前のデータです。gzipはmtime=0で保存しています。

```bash
python3 tools/benchmarks/drawing2d-incremental/analyze-production-retained.py docs/performance/data/retained-production-20261103 replayed.json
cmp replayed.json docs/performance/data/retained-production-20261103/summary.json
```

全26 archiveの展開hash/length、6 TRXの計711 test成功、283 C# fileの測定凍結hashを確認しています。NativeAOT smokeのsourceとprojectはchecks内に保存しています。projectを同じ `artifacts/retained-production-verification/aot-smoke/` へ展開すれば相対参照を再利用できます。
