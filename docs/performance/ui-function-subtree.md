# Function subtree update performance

2026-10-31、部分木更新の導入前（`3b6c54dd`）と関数境界方式の実装（`ed0afdc2`）を、同じハーネス・共有Linux環境で順次比較した。

| 兄弟関数数 | 導入前 ms/回 | 導入後 ms/回 | 導入前 B/回 | 導入後 B/回 |
| --- | ---: | ---: | ---: | ---: |
| 100 | 0.377217 | 0.041226 | 571776.00 | 11536.00 |
| 1000 | 5.620587 | 0.351317 | 6034136.00 | 18705.92 |

独立したlabelのSignalを持つカウンターをPanel上に重ね、一つのlabelだけを変更する。各bodyはローカルSignalと40×20のButtonを持つ。Signal代入からUpdateLayoutまでを含み、初期構築とassertは計測外とした。更新後のlabelと、無関係なroot・兄弟の参照保持を検証した。

Release / SDK 10.0.401 / tiering無効 / workstation GC。warmup 5回、100回/batchを5 batch実行し、batch平均の中央値を示す。測定中にbuildや別の測定を並行実行していない。割当はGC.GetAllocatedBytesForCurrentThreadによるもので、常駐メモリ量ではない。

[再実行用ハーネス](../../tools/benchmarks/ui-functions/README.md)と[環境・ソースchecksum・全batch](ui-function-subtree.json)を保存する。

この結果は疎な関数更新のCPU測定であり、全関数を同時更新する場合の速度、初期構築、常駐scopeの費用、実GPU、OS loop、p99遅延を測定したものではない。dirty判定とrevision集約は購読境界数、slot交換は兄弟数に比例する。入力geometryやlayoutの再計算が広がる場合もある。明示Rebuildや全体検証が必要な画面のフォールバックは[UI-0023](../adr/ui/UI-0023-function-widgets-and-local-signals.md)に従う。

## Element分離後の再測定

2026-10-31、最終スタックの実装 `bc01a8b3` （測定時のローカルrevision）を共有Linux x64 / Intel Xeon Platinum 8573Cで測定した。
.NET 10、TieredCompilation=0、gcServer=0、warmup 5回、100回×5 batchの中央値。
計測中はbuild・別の測定を実行せず、Signal代入からUpdateLayoutまでを含めた。

| 要素数 | 時間 µs/回 | managed B/回 |
| --- | ---: | ---: |
| 100 | 8.487 | 9080.00 |
| 1000 | 9.123 | 9049.92 |

更新値と無関係なroot・兄弟の保持を検証した。全batchと実装のSHA-256は同名JSONのelementSeparationへ保存し、公開コードとの対応はSHA-256で確認する。
過去の表は別revisionの測定履歴であり、今回と同時条件のbefore/after比較ではない。GPU、OS入力ループ、p99遅延は含めない。
label更新は保持されたElementのbodyを局所的に再評価し、geometryが変わらなければlayoutを再計算しない。
