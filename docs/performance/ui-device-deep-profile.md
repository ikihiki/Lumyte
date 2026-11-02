# Windows UIの詳細な時間・割当調査

2026-11-02、[120Hz改善後](ui-game120-performance.md)の製品コード`c506d0e0`と同じツリーを調査しました。
最大の固定費は、毎frameのsurface能力照会でした。全要素更新の追加割当は、主にWidgetの変更候補生成と更新計画に集中しています。
製品の動作変更は行わず、詳細probeと固定Window専用のcache切り分けを任意適用patchに保存しました。

## 条件と測定方法

RTX 4080 SUPER / Vulkan / driver 617.14、Ryzen 7 7800X3D、Windows build 26200、.NET 10.0.12、Release、既定tiered compilation・workstation GCです。
論理800×600、DPI 1.5、1200×900、標準UiApplicationとDesktop driver、要求120Hz、1,000矩形のgridです。
warmupは240 frame、通常の詳細runは各1,200 frame、EventPipeは1,800 frame、最後の独立したA/B比較は各600 frameです。
build・他の測定・解析を終えてから順次実行しました。途中のrunもローカルartifactsへ保持しています。
前回のwarmup 120・6,000 frameとは条件が異なり、run間のFPS差を新しい製品改善の効果とは扱いません。

probeはinclusiveの壁時計と同threadのmanaged allocationを測ります。nested scopeは重複し、GPU待ちとowner待機も重なります。
表の時間をすべて足してframe時間にはできません。非同期spanの割当は他の仕事を含み得るため、割当の判断には同期UI経路を使います。
詳細probeなし・完了通知だけの対照は約2.520 MB/frame、詳細probeありの全要素更新は約2.525 MB/frameでした。
追加割当は約0.2%ですが、per-frame結果の保持がGCの世代構成へ影響する可能性は残ります。120Hz制限下でFPSが同じでも計測負荷ゼロとはいえません。

## 最大の固定費: surface能力の再照会

[UiPlatformApplicationDriver.DrawAsync](../../../src/UI/Lumyte.UI.Application/UiPlatformApplicationDriver.cs)は、swapchainを保持しているframeでも毎回`UiHostPresenter.Configuration`を呼びます。
そこから[WgpuSurface.GetCapabilities](../../../src/Graphics/Lumyte.Graphics.Wgpu/WgpuSurface.cs)のnative照会へ進みます。

| 1,200 frameのcase | 能力照会native 平均ms | p95 ms | 記録開始→Submit完了 平均ms |
| --- | ---: | ---: | ---: |
| 1要素の色変更 | 2.66 | 3.30 | 3.10 |
| 全1,000要素の色変更 | 2.55 | 3.10 | 2.98 |
| 静止したまま提示 | 2.57 | 3.05 | 2.92 |

各caseで毎frame 1回です。時間のほぼすべてが`_native.GetCapabilities(_adapter)`内にあり、managedの形式選択や配列生成は小さい部分でした。
これはCPUから呼んだnative関数内の壁時計で、CPU実行時間、driver内のlock、OS照会の寄与までは分解していません。

固定Window専用の診断cacheで照会を省略すると、独立した600 frameのA/Bで次の差が出ました。

| 指標 | 通常照会 | 診断cache |
| --- | ---: | ---: |
| 能力照会native 平均ms/frame | 2.715 | 0（呼び出しなし） |
| 記録開始→Submit完了 平均ms | 3.146 | 0.438 |
| 記録開始→frame完了 平均ms | 4.720 | 2.043 |
| 平均Hz | 120.00 | 119.98 |

先の1,200 frameの比較でも、記録開始→Submit完了は3.10→0.46msでした。約2.7msの固定費を回避できることをA/Bで確認しました。
要求120Hzの上限があるのでFPS上昇は主張しません。処理と完了までの時間が短くなり、ownerの待機時間へ移っています。
診断cacheにはresize、monitor変更、surface/device再生成等の無効化方針がなく、そのまま製品へ反映する実装ではありません。

## 全要素更新の追加負荷

全1,000矩形が一つの色Signalを参照し、完了ごとにその値を切り替えるcaseです。
Elementを保持し、その設定候補Widgetを生成して検証・公開する経路を測りました。

| 同期UI処理 | 全要素更新の平均ms/frame | 割当 KB/frame（decimal） |
| --- | ---: | ---: |
| Signal通知・job登録 | 0.06 | 96.6 |
| effect実行 | 0.15 | 40.0 |
| 更新batch全体 | 0.72 | 2,093.8 |
| └ 候補生成・leaf計画準備 | 0.41 | 1,522.1 |
| └ 計画の検証 | 0.12 | 284.9 |
| └ Signal binding準備 | 0.07 | 120.0 |
| └ 公開・observer採用 | 0.11 | 128.0 |

batch内の行は内訳なので、batch全体へ加算しません。全要素更新の全割当約2.52 MB/frameの約83%がbatch内、約60%が候補生成・leaf計画の段階です。
1要素更新のbatchは約0.01ms・3.6KBでした。

[ApplySignalElements](../../../src/UI/Lumyte.UI/UiSurface.SubtreeUpdates.cs)の`previous.Recreate(new(element))`、`ValidateRoot`、`PrepareLeaf`が候補を作ります。
生成された`Rectangle.__LumyteCreate`は新しいWidget、factory用closure、Signal引数等を用意します。
EventPipeのallocation tickでも、このfactoryを最も近いLumyte frameに持つ割当推計が約1.86GB / 全推計4.54GBでした。
型サンプルにはclosure、Rectangle、dictionary/HashSetのentry配列、enumerator、ElementUpdate、SignalUpdateが現れました。
これは型別の正確な個数・byte数ではありません。改善の優先順位は、scopeで実測した候補生成の1.52 MB/frameを基準にします。

## layout・描画記録・GPU待機

| 処理 | 平均ms/frame | p95 ms | 条件 |
| --- | ---: | ---: | --- |
| layout.compute | 0.03 | 0.04 | 全要素の幅・色更新 |
| input.rebuild | 0.04 | 0.04 | 全要素の幅・色更新 |
| 描画tree走査 | 0.08 | 0.10 | 1要素の色変更 |
| DrawList固定化 | 0.01 | 0.01 | 1要素の色変更 |
| 1,000矩形のGPU用data pack | 0.09 | 0.11 | 1要素の色変更 |
| GPU submission完了待ち | 1.90 | 3.05 | 1要素の色変更 |
| その中のTask.Delayによるpoll待ち | 1.90 | 3.04 | 1要素の色変更 |

色変更だけではlayout.compute/input.rebuildは走りません。幅を全更新しても、このworkloadではlayout計算は支配的ではありません。
一方、1要素変更でもrootのDrawListを再記録し、走査で184.7KB、固定化で37.2KBを割り当てます。
静止caseでは走査・固定化を再利用しますが、矩形のdata packは毎frame残ります。

submission待ちの大半の壁時計は[WgpuSubmission.WaitAsync](../../../src/Graphics/Lumyte.Graphics.Wgpu/WgpuSubmission.cs)内の`Task.Delay(1)`から復帰するまでにあります。
この間にもGPUは動くので、「GPU処理は不要でpollだけが1.90ms」とは判断できません。GPU timestampは未採取です。
通知のpoll間隔、dispatcherへの復帰、GPU実行を分けた改善検討が必要です。

## 遅いframeとGC・スケジューリング

全要素更新の1,200 frameでGC開始カウント60、合計停止135.0ms、停止のあるframeの平均2.25ms・最大6.28msでした。
約20 frameに1回で、Gen1/Gen2を含む回数です。世代別のCollectionCountを足すと重複します。
停止と重なるframeの完了間隔p95は11.45ms、重ならないframeでも10.44msでした。

EventPipeの1,800 frameでは、GCの停止90回、scopeでの累積停止213.7msを確認しました。
完了間隔の遅い上位10 frame中7 frameにはGC停止の重なりがありません。
最遅13.06msのframeにも重なりがなく、GCだけですべての揺れを説明できません。
サンプラ等の`SuspendOther`とGCの`SuspendForGC`を分離し、イベント欠落数は0でした。

1要素更新では、描画記録の開始間隔p95は8.67ms、完了間隔p95は9.78msでした。
完了間隔には「開始間隔＋今回の完了latency−前回の完了latency」が現れるため、完了間隔の揺れと描画開始の遅れは同じ値ではありません。
ownerのtimer返却がdeadlineを超えた量は合計平均約0.28ms/frame・p95約0.52msでした。
owner.wait全体は意図的な120Hz待機や非同期待機も含み、処理負荷として加算しません。
OSのcontext switchは採取しておらず、timerやpollからの復帰遅延をscheduler・driver等へ完全には分解していません。

## 再現と次の改善順

最優先はsurface能力照会の再利用です。続いてWidget候補/factory・更新計画の割当削減、root全体のDrawList再記録の縮小、submission通知待ちの改善を検討します。
layout自体の最適化と静止矩形packの再利用は、今回の測定では優先度が下がります。

[実行手順と計測上の注意](../../tools/benchmarks/ui-device/README.md)を参照してください。
[全frameのgzip JSONL・集計・manifest](data/ui-deep-profile)、[EventPipeの解析結果](data/ui-deep-profile/trace-analysis.json)を保存しています。
生のnettraceはサイズとruntimeの詳細情報を含むためローカルartifactsへ保持し、SHA-256とツール版をmanifestへ記録しています。
traceのstackサンプルは待機中も採られるため、件数からCPU時間の割合を主張しません。
計測patchを戻した製品ソースに変更が残らないこと、通常ハーネスと解析器のbuild、実画素6ケースのhash一致、Markdown 271ファイルのlintを確認しました。
gzipの全11ファイルも展開後のSHA-256を照合しました。
