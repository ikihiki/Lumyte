# Lumyte.Signalsの単体テスト

Signalsの公開APIの契約と回帰を、host・GPU・BenchmarkDotNetへの依存なしで検証します。

| ファイル | 責務 |
| --- | --- |
| GraphTests | 値・依存グラフ、batch、scheduler、scope、循環と書込み制約 |
| AsyncTests | 完了、失敗、キャンセル、再取得、失効、scopeと配送 |
| InvalidationTests | 共有経路、動的分岐、停止中・待機中の無効化 |
| PassiveCacheTests | 未購読のキャッシュ、変更revision、失敗後の再評価 |
| DirectEvaluationTests | 追跡の復元、read set、schedulerの拒否 |
| DependencyLifetimeTests | 依存集合の所有権、再利用、例外・破棄後の参照解放 |

```sh
dotnet test tests/Core/Lumyte.Signals.Tests -c Release
```

コマンドはリポジトリルートで実行します。準備と全体の分類は[テスト一覧](../../README.md)を参照してください。測定ケース自体の検証は`../Lumyte.Signals.Benchmarks.Tests`、性能の比較は[ベンチマーク](../../../benchmarks/Lumyte.Signals.Benchmarks/README.md)が担当します。
