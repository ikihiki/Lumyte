# UI入力情報の保持と性能測定

測定日: 2026-10-29。対応: PR #80、[UI-0005](../adr/ui/UI-0005-pointer-dispatch-and-capture.md)。

## 最新コード: 部分木の入力領域による探索打ち切り

測定完了: 2026-10-29 15:23 JST。変更前はcommit `7045000ce9fea830466ea1522f8874dd21007bca`、変更後は `0c47a95e47da314e7fab4f35f6f32f4785d18919`。同じ測定コードの19ケースを各7 batch実行し、全build完了後に変更前→変更後の順で測った。

各Widgetに、自身と入力対象の子を含む部分木の入力矩形を保持する。UpdateLayoutの入力情報更新で合成し、hit testでは点を含まない部分木を飛ばす。子のoverflowを含め、clip・viewport・選択された入力対象・前後関係を維持する。UiSurfaceやWidgetにButton等の具体型判定を追加していない。

### 結果

単位はµs/Dispatch。Moveの入力recordは再利用し、全ケースの内部managed割当は変更前後とも0Bだった。

| ツリー | 要素数 | 変更前 captureなしMove | 変更後 captureなしMove | 変更前 capture中Move | 変更後 capture中Move |
| --- | --- | --- | --- | --- | --- |
| small | 97 | 1.167 | 0.277 | 0.016 | 0.017 |
| typical | 769 | 8.946 | 1.186 | 0.016 | 0.017 |
| wide | 6,145 | 106.763 | 8.356 | 0.017 | 0.017 |
| large | 10,241 | 183.462 | 16.319 | 0.016 | 0.017 |
| deep | 67 | 1.132 | 1.247 | 0.016 | 0.017 |
| eight-pointers | 769 | 9.267 | 1.164 | 0.016 | 0.017 |

10,241要素のcaptureなしMoveの処理時間を約1/11.2へ短縮した。最初の行へ到達するまで他の兄弟は調べるが、点を含まない行の子孫へは降りなくなった。deepのように一本の祖先列をすべて辿るケースでは探索を減らせず、判定が増えるため今回の値も少し悪化している。重なりや多数の兄弟では最悪O(Widget数)が残る。

### 更新コストと押下

| 処理 | 要素数 | 変更前 µs/操作 | 変更後 µs/操作 | 変更前 B/操作 | 変更後 B/操作 |
| --- | --- | --- | --- | --- | --- |
| Down/Upの1 Dispatch平均 | 769 | 4.532 | 0.602 | 16 | 16 |
| Down/Upの1 Dispatch平均 | 10,241 | 90.076 | 8.344 | 16 | 16 |
| 入力情報のみ更新 | 769 | 36.854 | 40.444 | 64 | 64 |
| 入力情報のみ更新 | 10,241 | 476.746 | 553.177 | 64 | 64 |
| layout＋入力情報更新 | 769 | 263.267 | 265.892 | 138,440 | 138,440 |
| layout＋入力情報更新 | 10,241 | 4181.287 | 4331.952 | 2,121,888 | 2,121,888 |
| Move＋record生成 | 769 | 0.022 | 0.021 | 40 | 40 |

10,241要素の入力情報更新は約0.477ms→0.553ms、layout込みは約4.18ms→4.33msとなった。更新時の矩形合成と常駐フィールドが追加コストである。入力情報更新のmanaged割当はcapture 1本の条件で64B/回を維持し、更新ごとに部分木矩形の配列を生成しない。追加フィールドはWidget当たりdouble 4個分だが、Widget構築時の割当と常駐メモリ全体は今回測っていない。

### 高頻度入力への換算

| 件/s | 入力間隔 µs | 変更前 captureなしMoveの時間割合 | 変更後 captureなしMoveの時間割合 |
| --- | --- | --- | --- |
| 1,000 | 1000 | 18.346% | 1.632% |
| 8,000 | 125 | 146.770% | 13.055% |

上表は10,241要素の中央値から単一UIスレッドの経過時間予算を換算した値で、実機8,000Hzの配送試験やOSのCPU使用率ではない。描画・OS入力・新規ツリー構築・reconciliation・最悪遅延は測定対象外である。

条件は前回と同じRelease / .NET 10.0.12 / SDK 10.0.401、Workstation GC、tiering無効、Debian 13 x64、Xeon Platinum 8573C、CPU quota 4。128回warmup、32回pilot、約100ms/batchを目標に7回測り、各batch平均の中央値を示す。CPUを専有せずaffinityも固定しない。Down/Upは一組を計時して2で割り、入力情報更新はcapture 1本、captureなしMoveはviewport内のButton内容上とButton外を交互に送る。

変更前large Moveのbatch平均は177.017〜317.420µs、変更後は16.116〜17.945µsだった。共有環境の揺れを含むため、小差を確実な改善と断定せず、単一イベントのp99/最大遅延とも扱わない。

- [変更後の全batch・割当・Gen0回数](data/ui-input-subtree.json)
- [変更前を同時期に再測定した生データ](data/ui-input-before-subtree.json)
- [測定コード](../../tools/benchmarks/ui-input/Program.cs)／[再実行手順](../../tools/benchmarks/ui-input/README.md)

## 汎用イベント配送版（部分木領域の導入前）

測定完了: 2026-10-29 14:50 JST。UIコードはcommit `b4595fd7d794984e61f557209d3e25e28d9ef1d4`、比較対象は汎用イベント化直前の `8e3602a5397d157cd1b6e2386baadab88e4c3512`。両方へ同じ測定ソースを接続し、各buildを完了してから、比較対象→最新コードの順に別processで測った。製品コードは変更していない。

capture中Moveの内部割当0Bは維持できている。一方、captureなしMoveは新たにhit testとOnInputの親への伝播を行い、10,241要素では約192µs/件だった。8,000件/sの125µs間隔を上回るため、全入力がこの経路を通る場合は単一UIスレッドで処理し切れない。今回の測定では間引き・最適化を加えていない。

Release / .NET 10.0.12 / SDK 10.0.401、Workstation GC、DOTNET_TieredCompilation=0、Debian 13 x64、Xeon Platinum 8573C、CPU quota 4。共用環境でaffinity固定はしない。128回warmup、32回pilot、各batch約100msを目標に7回測り、表はbatch平均の中央値とする。ツリー構築・初回UpdateLayoutは定常入力の計時外。描画・OS入力配送・event-to-present遅延も対象外である。

### Moveの処理時間

単位はµs/Dispatch。入力recordは再利用し、下記全ケースの最新コードの内部managed割当は0Bだった。

| ツリー | 要素数 | 直前版 capture中 | 最新 capture中 | 直前版 captureなし | 最新 captureなし |
| --- | --- | --- | --- | --- | --- |
| small | 97 | 0.012 | 0.021 | 0.008 | 1.349 |
| typical | 769 | 0.012 | 0.020 | 0.009 | 10.919 |
| wide | 6,145 | 0.015 | 0.017 | 0.009 | 115.026 |
| large | 10,241 | 0.013 | 0.017 | 0.008 | 192.440 |
| deep | 67 | 0.014 | 0.017 | 0.009 | 1.154 |
| eight-pointers | 769 | 0.014 | 0.017 | 0.010 | 9.608 |

captureなしは、Buttonの内容上 `(5, 5)` とButton外 `(500, 5)` を交互に配送する。どちらもviewport内で、既定handlerでは未処理になることを全件確認した。viewport外の即時returnを測るケースではない。最初の行に向けたhit testは、前面から先に並んだ他の行も探索する。直前版はcaptureなしMoveを即座にfalseで返しており、同じ量の処理を比較しているわけではない。

latest largeのcaptureなしMoveは7 batchの平均値が189.888〜212.023µsだった。typicalのlayout＋入力更新は255.618〜536.744µsと揺れている。共有環境のため小差を改善率と断定せず、単一イベントのp99や最大遅延としても扱わない。

### その他の処理と割当

| 処理 | 要素数 | 直前版 µs/操作 | 最新 µs/操作 | 直前版 B/操作 | 最新 B/操作 |
| --- | --- | --- | --- | --- | --- |
| Move＋record生成 | 769 | 0.022 | 0.024 | 40 | 40 |
| Down/Upの1 Dispatch平均 | 769 | 5.952 | 5.022 | 16 | 16 |
| Down/Upの1 Dispatch平均 | 10,241 | 103.507 | 98.247 | 16 | 16 |
| 入力情報のみ更新 | 769 | 46.626 | 43.867 | 24 | 64 |
| 入力情報のみ更新 | 10,241 | 649.334 | 495.427 | 24 | 64 |
| layout＋入力情報更新 | 769 | 257.903 | 335.558 | 138,400 | 138,440 |
| layout＋入力情報更新 | 10,241 | 4496.979 | 4488.904 | 2,121,848 | 2,121,888 |

Down/Upは一組を計時して2 Dispatchで割った平均であり、Down単独の時間ではない。10,241要素では一組約196.49µsとなる。record生成込みのMoveは40B/件で、8,000件/sなら約320KB/s。入力情報の更新はcaptureを一つ保持した条件で、更新識別子と通知対象を退避する配列を含め64B/回だった。独自handlerの処理や子列によって時間・割当は別途増える。

### 1,000/8,000件/sへの換算

10,241要素の中央値から、単一UIスレッドの経過時間予算を換算する。実デバイスでの連続配送試験やOSのCPU使用率ではない。

| 件/s | 間隔 µs | capture中Moveの時間割合 | captureなしMoveの時間割合 |
| --- | --- | --- | --- |
| 1,000 | 1000 | 0.002% | 19.244% |
| 8,000 | 125 | 0.013% | 153.952% |

課題はcaptureなしMoveのツリー探索と親への伝播である。ここをcapture中Moveの数値で代表させない。layout＋入力情報更新も約4.49ms/回で、新規ツリーの構築・描画を含めたframe時間は別途評価する必要がある。

- [最新コードの全batch・割当・Gen0回数](data/ui-input-generic.json)
- [直前版を同時期に再測定した生データ](data/ui-input-before-generic.json)
- [測定コード](../../tools/benchmarks/ui-input/Program.cs)／[再実行手順](../../tools/benchmarks/ui-input/README.md)

## 過去の測定記録

### 入力snapshot版の結果

入力用の子列・geometry・clipを表示状態ごとに確定し、capture中のMoveは保持したButtonの領域を参照する形へ変更した。変更前はMoveごとに全captureの祖先と子列を繰り返し検証し、List・HashSet・配列を割り当てていた。

表示状態が変わらないMoveでは、ツリーの大きさ・深さ・capture数による走査と内部のmanaged割当をなくせた。8,000件/s換算の定常処理は下記の時間予算内に収まる。一方、全viewの再作成はツリーに比例し、layoutと併せた更新の時間・割当は変更前より増える。描画だけの変更では入力viewを無効化せず、変更のない入力間で再利用することが前提である。

### 条件と方法

- 共用Linux環境、Intel Xeon Platinum 8573C、Debian 13 x64、.NET 10.0.12 / SDK 10.0.401、CPU quota 4。
- Release build、Workstation GC、DOTNET_TieredCompilation=0。CPU専有・affinity固定は行っていない。
- 変更前はcommit `8a651c1465860269da7ccb73e7191e0c10ed9638`、変更後は本PRのinput snapshot再利用実装。同一の[測定コード](../../tools/benchmarks/ui-input/Program.cs)をそれぞれのUI assemblyに接続した。
- 128回のウォームアップ後に32回で反復数を調整し、各batch約100msを目標に7回測定した。batch内は入力を間引かず連続Dispatchする。表は7つのbatch平均値の中央値であり、単一イベントのp99や最大遅延ではない。
- Stopwatchの経過時間とGC.GetAllocatedBytesForCurrentThreadを使用する。入力recordを再利用するdispatch本体と、record生成も含める場合を分ける。
- ツリーは縦Stackの各行にButtonと20×10の内容を置き、Panelでdepth段包む。幅16/128/1,024/2,048、depth 4/4/4/3で97/769/6,145/10,241要素。deepは幅1・depth64、eight-pointersは769要素に8本をcaptureし順番にMoveする。
- MoveはButtonの内側と外側を交互に配送し、全ての入力がcaptureへ消費されたことを確認する。Down/Upは2回のDispatchをまとめ、1 Dispatch当たりへ換算する。
- layout-and-input-refreshはviewport幅を511/512へ交互に変え、既存ツリーのlayoutと入力viewを更新する。input-only-refreshは新しいprotectedなInvalidateInputだけを呼び、layoutを省いた入力view準備を測る。後者のhookは測定開始前にdelegate化し、反射呼出し自体を計時しない。
- 新しいWidgetツリーの構築・reconciliation、実OS/Browserの入力配送、描画・GPU・Present、実機でのevent-to-present遅延は測っていない。1,000/8,000Hzは測った処理時間からの時間予算換算であり、実デバイスでの連続入力試験ではない。

### 定常Move

単位はµs/Dispatch、B/Dispatch。入力recordは再利用する。

| ツリー | 要素数 | 変更前 µs | 変更後 µs | 変更前 B | 変更後 B |
| --- | --- | --- | --- | --- | --- |
| small | 97 | 8.436 | 0.027 | 10,616 | 0 |
| typical | 769 | 22.029 | 0.027 | 38,744 | 0 |
| wide | 6,145 | 129.962 | 0.026 | 300,800 | 0 |
| large | 10,241 | 268.156 | 0.028 | 616,232 | 0 |
| deep | 67 | 73.244 | 0.027 | 99,656 | 0 |
| eight-pointers | 769 | 119.328 | 0.027 | 219,064 | 0 |

入力recordの生成も含めるtypicalは、変更後0.032µs・40B/件だった。8,000件/sならrecord生成による割当は約320KB/sとなる。Down/Upを繰り返すtypicalは、1 Dispatch平均0.436µs・16Bであり、capture開始時の管理objectは割り当てる。

### 1,000/8,000Hzの時間予算換算

10,241要素・Moveの結果を換算する。百分率はUIスレッドの経過時間を1秒に対して換算した値であり、OSのCPU使用率を測った値ではない。100%を超える変更前の8,000Hzは、同じ条件の単一スレッドでは入力を捌き切れない。

| 件/s | 入力間隔 µs | 変更前の時間割合 | 変更後の時間割合 |
| --- | --- | --- | --- |
| 1,000 | 1000.0 | 26.816% | 0.003% |
| 8,000 | 125.0 | 214.525% | 0.022% |

### view変更時

単位はms/世代更新、MiB/世代更新。定常Moveの結果とは別に評価する。

| ツリー | 要素数 | 変更前 layout+input ms | 変更後 layout+input ms | 変更後 inputのみ ms | 変更後 layout+input MiB | 変更後 inputのみ MiB |
| --- | --- | --- | --- | --- | --- | --- |
| typical | 769 | 0.227 | 0.277 | 0.058 | 0.283 | 0.139 |
| large | 10,241 | 3.888 | 4.755 | 1.130 | 4.422 | 2.242 |

10,241要素の更新全体は約4.75msで、60fpsの16.67ms・120fpsの8.33msからその時間を使う。入力snapshotだけでも約1.13msかかるため、8,000Hzの入力ごとには作らない。更新中に到着する入力はhostのqueue/coalescing方針に依存する。全ツリー更新が毎frame発生する場合は上記のmanaged割当と残りの構築・描画時間も評価する必要があり、画面全体の120fpsや最悪入力遅延を保証する結果ではない。

### 生データと再実行

- [変更前の全batch・割当量・Gen0回数](data/ui-input-before.json)
- [変更後の全batch・割当量・Gen0回数](data/ui-input-after.json)
- [実行方法と比較手順](../../tools/benchmarks/ui-input/README.md)

### Widget内保持版（Button探索分離前の測定）

独立した入力snapshotとButton矩形mapを削除し、各Widgetへ入力矩形・入力用の子列・最寄りButton・更新識別子を保存する。UpdateLayoutで入力情報も更新し、Dispatchから準備・layout呼出しを除いた。layoutの幾何も計算したWidgetへ直接反映し、更新失敗時のロールバックを行わない。子のlayout処理漏れ等を検査するprotocol tableは残る。

同じ環境・Release・tiering無効・7 batchの方法で再測定した。初回UpdateLayoutは入力配送前の準備へ移動し、定常Dispatchの計時には含めない。以下のsnapshot版は上記の前回測定値であり、同時測定ではない。

| 項目 | 要素数 | snapshot版 µs/操作 | Widget内保持 µs/操作 | snapshot版 B/操作 | Widget内保持 B/操作 |
| --- | --- | --- | --- | --- | --- |
| capture中Move | 769 | 0.027 | 0.012 | 0 | 0 |
| capture中Move | 10,241 | 0.028 | 0.012 | 0 | 0 |
| Down/Upの1 Dispatch平均 | 769 | 0.436 | 4.646 | 16 | 16 |
| layout＋入力情報更新 | 769 | 277.219 | 252.405 | 296,416 | 138,400 |
| layout＋入力情報更新 | 10,241 | 4754.937 | 4242.054 | 4,636,374 | 2,121,848 |
| 入力情報のみ更新 | 769 | 58.254 | 39.061 | 145,736 | 24 |
| 入力情報のみ更新 | 10,241 | 1130.251 | 558.138 | 2,350,846 | 24 |

約1万要素の入力情報更新は約0.558ms・24B/回となった。24Bは更新識別子で、各ノードの入力矩形・全体配列・Button辞書を更新ごとに割り当てない。通常のWidgetChildrenは直接参照する。独自の子列は更新時に検証・保存するため、コンポーネントにより別途割当が生じる。

一方、Downは平坦な配列ではなく子ツリーを前面から再帰探索するため、この769要素の測定ではsnapshot版より遅くなった。capture中MoveはButtonの保存値を直接参照し、内部割当0Bを維持する。record生成込みのtypical Moveは約0.018µs・40B/件だった。

入力情報はWidgetの寿命中に保持するフィールドとなる。上表の割当は更新操作中の値であり、新しいWidgetツリーの構築コストや常駐メモリ全体を測ったものではない。8,000Hzでの実機入力・描画遅延も引き続き対象外とする。

[Widget内保持版の生データ](data/ui-input-widget.json)

#### Button探索の分離

その後、Widgetに保存していた最寄りButtonの参照と、更新時のButton型判定を削除した。Widgetは入力領域と子の探索だけを扱い、Button側がDown時にhit要素から所有親をたどって操作対象を選ぶ。capture中Move/Upの領域参照は維持する。上記のWidget内保持版の数値はこの分離前のcommit `35d7449daf1894deda030d6c6b8fe161f5d2daff` の測定であり、分離後のDownや入力情報更新の測定結果ではない。

続く汎用イベント配送への変更では、UiSurfaceからButton認識も除き、WidgetのOnInputとcapture通知へ移した。上記の測定値はこのイベント配送API導入前の記録であり、現在のhandler配送や取消通知を含む性能値ではない。
