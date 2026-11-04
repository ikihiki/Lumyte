# 公開保持APIのレビュー計測データ

測定sourceは `916b6de`、CPU比較元は `1c4470d10d83ced90c78b06ff2ec0e7b53be38aa`。同じ公開API probe sourceを使い、before CPU→after CPU/GPU→after逆順→before CPU逆順の4独立processを保存しています。warmup10/sample20、計CPU480 frame・GPU800 frame・画素比較120です。

`manifest.json` のmeasureは採用したrawと環境、verificationは729単体test・既存GPU59比較・AOT・hardware guard等です。全23gzipの展開hash/length、6TRXの成功数、測定開始時から不変の245 C# source hashを検証しています。software negative controlは意図した非ゼロ終了で、性能集計へ含めません。

公開ログはhome/checkout/host/userを伏せています。`sha256`/`bytes`は展開後の公開コピー、`originalSha256`は伏せる前のデータです。gzipはmtime=0です。

```bash
python3 tools/benchmarks/drawing2d-incremental/analyze-public-retained-api.py docs/performance/data/retained-public-api-20261103 replayed.json
cmp replayed.json docs/performance/data/retained-public-api-20261103/summary.json
```

UIのCPU profileはflat Panel、GPU profileはgrid Stackです。scene内部ノード数を公開APIから取得できないUI行はnullのままにし、実UI要素数を別に記録します。実issued draw数は転送したencoder APIの呼出し数で、driver全体の計数やGPU占有率ではありません。
