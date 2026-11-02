# UI performance optimizations

2026-11-01、PR #71の性能改善案を反映したUIスタックを、同じハーネス・同じマシンで変更前後比較しました。非表示animationの問い合わせ、局所的な幅更新、無変更frameのCPU描画命令再利用、shared-element検索が軽くなりました。一方、10,000要素で毎回描画命令を生成するhostの時間は測定回ごとに差が変わり、明確な速度改善とは言えません。各回とも割当は減少しています。

## 測定対象と環境

| 項目 | 値 |
| --- | --- |
| baseline本体 | `6b4c13c02c80e9ef0503a0c87e235ae27e896c13` |
| optimized本体 | `21e15153c23bcd740407c32fc08936d07db6c3a2` |
| ハーネス追加commit | `e7682b4f` |
| OS・CPU | Debian 13 x64、Intel Xeon Platinum 8573C |
| CPU・メモリ制限 | CPU quota 4、16 GiB |
| .NET | SDK 10.0.401、runtime 10.0.12 |
| 構成 | Release、tiered compilation無効、workstation GC |

公開時は変更を各担当PRへ再配置するためcommit SHAが変わりますが、本体コード・ハーネスは上記の測定時と同一です。後述するlayout差し戻しの候補は原因切り分け用で、本体へは採用していません。元のレビュー環境（AMD EPYC 9V45）とはCPUが異なり、レビュー中の絶対値との差を改善量とは扱いません。

測定は両buildと他agentのbuild・testの完了後に順次実施しました。baselineの実行ディレクトリと依存ファイルを保存し、optimizedを同じ環境でbuildした後、baseline、optimizedの順に全caseを実行しています。hostのみの再測定はoptimized、baselineの順です。

[ハーネスと実行手順](../../tools/benchmarks/ui-optimization/README.md)を保存しています。各caseはwarmup後5バッチを取り、バッチ内の1回当たり平均値の中央値を表に示します。`Stopwatch.GetElapsedTime`と`GC.GetAllocatedBytesForCurrentThread`を使用し、GCは各バッチの直前に実行します。構築・assertion・出力は計測外です。回数・warmup・全バッチはJSON Linesに記録しています。時間の統計的信頼区間は算出していません。

## 全caseの比較

[baseline生データ](ui-optimization-baseline.jsonl)、[optimized生データ](ui-optimization-optimized.jsonl)。時間の単位はms/回、割当はB/回です。

| 処理 | baseline ms | optimized ms | baseline B | optimized B |
| --- | ---: | ---: | ---: | ---: |
| empty-element-construction | 0.000032 | 0.000043 | 248 | 272 |
| hidden-pending-100 | 0.142300 | 0.001640 | 0 | 0 |
| hidden-running-100 | 0.132940 | 0.001380 | 0 | 0 |
| hidden-pending-1000 | 13.309080 | 0.014520 | 0 | 0 |
| hidden-running-1000 | 13.027040 | 0.014140 | 0 | 0 |
| hidden-pending-5000 | 408.016180 | 0.116920 | 0 | 0 |
| hidden-running-5000 | 416.654860 | 0.096920 | 0 | 0 |
| animation-sparse-1000 | 0.197777 | 0.142498 | 132,510 | 4,630 |
| animation-dense-1000 | 3.206348 | 2.513913 | 4,011,632 | 2,750,256 |
| width-flat-10000 | 2.542052 | 1.591354 | 1,187,658 | 3,904 |
| width-grouped-10000 | 2.236844 | 0.906060 | 1,196,390 | 3,968 |
| host-static-1000 | 0.155502 | 0.003873 | 235,762 | 3,136 |
| host-color-1000 | 0.191691 | 0.227651 | 241,674 | 223,522 |
| host-custom-1000 | 0.151903 | 0.150670 | 235,762 | 219,250 |
| host-static-10000 | 3.382273 | 0.003873 | 2,593,340 | 3,136 |
| host-color-10000 | 2.706737 | 3.914534 | 2,599,252 | 2,335,242 |
| host-custom-10000 | 2.289804 | 3.752746 | 2,593,340 | 2,330,970 |
| shared-1000-8 | 0.685604 | 0.001392 | 442,208 | 336 |
| shared-10000-8 | 9.705792 | 0.000781 | 6,590,501 | 336 |

空の派生Elementは同じWidget宣言から構築し、既存配列に保持した1個当たりの値です。layoutの作業状態をElementへ移したことで、構築割当は248 → 272 B（+24 B、約9.7%）になりました。10,000個ならこの差だけで約240 KBです。全Widget型、オブジェクトグラフ全体、GC heapの常駐量やworking setを測った数値ではありません。更新ごとのlayout辞書・集合割当を減らす代わりにElementの常駐フィールドが増えるトレードオフです。

hiddenは親Panelを非表示にした独立producer群のpending/running getterです。問い合わせ前の更新や描画は含みません。非表示中の問い合わせがfalseとなり、再表示すると未完了状態が検出できることを確認しています。幅animationは共有producerで1,000葉の全部または一つを更新し、仮想60 HzのAdvanceAnimationsとUpdateLayoutを含みます。

幅Signal更新は平坦10,000葉、または100×100葉から一つを変更します。値代入とUpdateLayout（UpdateTreeを含む）を計測し、既存Element identityと新しい幅を確認しています。groupedの改善幅を、任意の木の形や変更数へ一般化するものではありません。

sharedは8 keysに対するResolveTargetを3巡し、ResolveTargetsを1回呼ぶ処理です。Element identityと対象集合を確認しています。索引の初期構築はwarmupに含まれ、毎frameの索引再構築やframe全体の速度を測ったものではありません。

## hostの逆順再測定

初回の10,000要素color/customでは時間増加とバッチ間のばらつきがあったため、buildを挟まず同じbinaryを逆順で実行しました。[baseline再測定](ui-optimization-host-baseline-repeat.jsonl)、[optimized再測定](ui-optimization-host-optimized-repeat.jsonl)にも全バッチを残し、初回の結果を置き換えていません。

| 処理 | baseline ms | optimized ms | baseline B | optimized B |
| --- | ---: | ---: | ---: | ---: |
| host-static-1000 | 0.164592 | 0.003458 | 235,762 | 3,136 |
| host-color-1000 | 0.191411 | 0.153823 | 241,674 | 223,522 |
| host-custom-1000 | 0.154372 | 0.133145 | 235,762 | 219,250 |
| host-static-10000 | 2.725856 | 0.005058 | 2,593,340 | 3,136 |
| host-color-10000 | 2.203760 | 2.613580 | 2,599,253 | 2,335,242 |
| host-custom-10000 | 2.330212 | 2.884846 | 2,593,340 | 2,330,970 |

staticは各測定720frame（warmup120 + 5×120）を全てdriverへ提出し、optimizedでは720回ともDrawList identityを再利用しました。color/customは720frameとも記録し直し、再利用回数は0です。customは派生Panelをrootに置いた対照で、末尾にcustomを置いた場合の適格性探索や任意のcustom描画処理のコストは測っていません。

10,000要素colorの時間は初回2.707 → 3.915 ms、再測定2.204 → 2.614 ms、customは初回2.290 → 3.753 ms、再測定2.330 → 2.885 msでした。割当は両回とも減少していますが、この2回ではCPU時間が増えているため、以下の追加切り分けを行いました。

## layout常駐状態を除いた候補との切り分け

+24 BのElement常駐状態がhostの時間増加に影響しているかを確かめるため、optimized本体から`UiElement.Layout.cs`と`UiLayoutPass.cs`だけをbaselineへ戻し、UI本体DLLだけを再buildした候補を作りました。その他の依存DLL・ハーネスはoptimizedの保存済みbinaryをそのまま使っています。この候補のhost smoke成功後、候補、元のoptimized保存済みbinary、baseline保存済みbinaryの順にhostを実行しました。測定中のbuildはありません。

[候補の生データ](ui-optimization-host-no-layout-fields.jsonl)、[直後のoptimized](ui-optimization-host-optimized-after-layout-ablation.jsonl)、[その直後のbaseline](ui-optimization-host-baseline-after-layout-ablation.jsonl)。時間の単位はms/回です。

| 処理 | baseline | optimized | layout差し戻し候補 |
| --- | ---: | ---: | ---: |
| host-static-1000 | 0.151047 | 0.003284 | 0.003536 |
| host-color-1000 | 0.177438 | 0.156090 | 0.147884 |
| host-custom-1000 | 0.149679 | 0.126890 | 0.134285 |
| host-static-10000 | 2.078814 | 0.003290 | 0.003188 |
| host-color-10000 | 2.077608 | 2.069857 | 2.134452 |
| host-custom-10000 | 2.030516 | 2.012962 | 2.011967 |

この直近の対では10,000要素colorがbaseline2.078 → optimized2.070 ms、customが2.031 → 2.013 msとほぼ同等でした。layoutを差し戻した候補はそれぞれ2.134 / 2.012 msで、常駐状態を除くことによる一貫した改善は確認できません。候補の恒久採用はしていません。

初回・逆順・切り分けの全結果を残し、好都合な回のみで速度改善率を示していません。color/customのCPU時間は共有環境で変動し、今回の計測から安定した改善・悪化のどちらも断定しません。割当は全測定で同じ傾向となり、10,000要素colorは約2.60 → 2.34 MB/回、customは約2.59 → 2.33 MB/回でした。無変更frameの命令再利用は全測定で大きく改善しています。

## 検証範囲と追跡情報

hostは実際のUiApplication.Turnを同一owner threadで手動進行し、同期fake driverへ毎frame提出しています。Surface.Record単体ではありません。初期化とprivate delegateの解決は測定前だけ行い、既存friend assemblyを用いた独立実行ファイルで本体のpublic APIを増やしていません。各バッチ後に全命令の色・geometry・transform・clip、最新と直前frameの色、提出回数を検証し、古い描画命令の再利用を検出します。全測定とsmokeのassertionは成功しました。

実GPUの描画、GPU待機、実OS windowの画面提示、pixel fill、OS入力、入力から表示までの遅延は測っていません。texture poolのGPU性能やFPS改善を、このCPU測定の結果から主張することはできません。

以下はSHA-256です。本体digestは`git ls-tree -r <SHA> src/UI src/Animation src/Graphics src/Core src/Input src/Platform`の標準出力、ハーネスdigestはファイル内容から算出しました。

| 対象 | SHA-256 |
| --- | --- |
| baseline本体対象ディレクトリ | `5d1b09c9c6514f394f4f96fe6c23cab08a59f12554f005b18f5d4724fea4005a` |
| optimized本体対象ディレクトリ | `7d876d06a3077a45277d539cc9e9c79b489b353d4f1981f7d1ffc7a2c830579c` |
| `tools/benchmarks/ui-optimization/Program.cs` | `fd3dcb016cbdf33d5060a3e24af41716590fea179d0c78b70aa4c4da24690bec` |
| baseline `Lumyte.UI.dll` | `961554a8d96cfa904ab074a3e323dae864535983a28bf4113eb1673ab1d2852b` |
| optimized `Lumyte.UI.dll` | `6a90c886dbf70261ff84bc84159215f1b9739df999fde461b53ec1f161462e3a` |
| layout差し戻し候補 `Lumyte.UI.dll` | `52026fbe83e5ae54533ced3b38baa0a4a3f675cb683cc9c1a95c28b18fd05b7d` |
