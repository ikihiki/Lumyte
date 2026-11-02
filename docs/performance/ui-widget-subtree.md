# All-widget subtree update performance

2026-10-31、関数境界方式（`ed0afdc2`）と全Widget方式（`ac0e53ce`）を、同じハーネス・共有Linux環境で順次比較した。

| 処理 | 要素数 | 導入前 ms/回 | 導入後 ms/回 | 導入前 B/回 | 導入後 B/回 |
| --- | ---: | ---: | ---: | ---: | ---: |
| Rectangleの幅 | 100 | 0.152238 | 0.062943 | 230512.00 | 31024.00 |
| Rectangleの幅 | 1000 | 1.545365 | 0.545083 | 2418696.00 | 223848.00 |
| 関数のlabel | 100 | 0.041837 | 0.040178 | 11536.00 | 14512.00 |
| 関数のlabel | 1000 | 0.363546 | 0.309275 | 18705.92 | 28881.92 |

Rectangleは独立したwidth Signalを持ち、Panel上に重ねて配置する。高さ10、幅10〜29、viewport 800×600で、一つの幅の変更からUpdateLayoutまでを含む。変更前は関数外の引数なので全体を生成し直し、変更後は対象のRectangleだけを生成する。最新値と無関係なroot・兄弟の保持をassertした。

関数は既存の40×20カウンターのlabelだけを変更する比較で、変更前後とも部分更新である。新しい投影検証・入力参照の維持を含め、1,000要素の時間は0.364ms→0.309ms、割当は18.7KB→28.9KBだった。割当の増加も結果として残し、全ケースで割当が減るとは扱わない。

Release / SDK 10.0.401 / tiering無効 / workstation GC。warmup 5回、100回/batchを5 batch実行し、batch平均の中央値を示す。測定とbuildは並行実行していない。割当はGC.GetAllocatedBytesForCurrentThreadによるもので、常駐メモリ量ではない。

[通常Widgetのハーネス](../../tools/benchmarks/ui-widgets/README.md)、[関数のハーネス](../../tools/benchmarks/ui-functions/README.md)、[環境・ソースchecksum・全batch](ui-widget-subtree.json)を保存する。

初期構築、購読境界数に伴う常駐メモリ、全要素の同時変更、GPU、OS loop、p99遅延は未測定。dirty判定とrevision集約は購読境界数、slot交換と兄弟key検証は兄弟数に比例する。全体validatorがNodesを読む場合は投影ツリーの全走査が必要になる。layoutと入力の再計算範囲は宣言の生成範囲と別に扱う。

この記録はPR分割変更前の上記commitで採取した履歴である。分割後のcommitの再測定結果とは扱わない。

## Element分離後の再測定

2026-10-31、最終スタックの実装 `bc01a8b3` （測定時のローカルrevision）を共有Linux x64 / Intel Xeon Platinum 8573Cで測定した。
.NET 10、TieredCompilation=0、gcServer=0、warmup 5回、100回×5 batchの中央値。
計測中はbuild・別の測定を実行せず、Signal代入からUpdateLayoutまでを含めた。

| 要素数 | 時間 µs/回 | managed B/回 |
| --- | ---: | ---: |
| 100 | 24.325 | 18160.00 |
| 1000 | 203.656 | 131784.00 |

更新値と無関係なroot・兄弟の保持を検証した。全batchと実装のSHA-256は同名JSONのelementSeparationへ保存し、公開コードとの対応はSHA-256で確認する。
過去の表は別revisionの測定履歴であり、今回と同時条件のbefore/after比較ではない。GPU、OS入力ループ、p99遅延は含めない。
幅更新はlayoutを伴うため、親の子列走査とlayout passの作業量・割当は要素数に依存する。
