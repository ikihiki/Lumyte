# WindowsのゲームUI向け120Hz改善

2026-11-01〜02、[前回のshaderキャッシュ・汎用shader改善](ui-device-performance.md)に続き、単色矩形のinstance描画と標準Desktop hostのframe待機を改善しました。
描画処理はPR #79、hostはPR #89、測定ハーネスと生データはPR #97へ配置します。

追加の[時間・割当・GCの詳細調査](ui-device-deep-profile.md)で、毎frameのsurface能力照会とWidget候補生成の負荷を分解しています。

## 条件

| 項目 | 条件 |
| --- | --- |
| 比較元 | 公開済み最終head `f68dbb3d1aa52622eb78899ef12d544dabb92480` |
| 描画・hostの実装元 | `4264635c` / `d2353d60`、各担当PRへcherry-pick後に通常mergeで伝播 |
| CPU | Ryzen 7 7800X3D、8 core / 16 thread |
| GPU | RTX 4080 SUPER、wgpu-native / Vulkan、driver 617.14 |
| OS / runtime | Windows 11 build 26200、.NET 10.0.12、SDK 10.0.401、Slang 2026.19、Release |
| runtime設定 | tiered compilation既定、workstation GC |
| Window | 論理800×600、DPI 1.5、1200×900、提示は既定FIFO |
| 実行経路 | 標準UiApplication + Desktop driver、FrameInterval = 1/120秒 |
| 要素 | 100列のStackとRectangle、単一Window、viewport全体を覆うgrid |

平均Hzは「1,000 / 完了間隔の平均ms」です。GPU実行時間・scanout時刻・入力から表示までの遅延ではありません。
完了間隔にはUI更新、layout、記録、提出、GPU通知、資源解放、OSとGCによる遅れを含みます。
未提示のstartup frameは別に数え、指定した測定frame数に到達しなければハーネスを失敗させます。
build・test・他の測定を終えてから順次実行し、frameの外れ値は除外しません。

## 標準hostの結果

最終比較ではwarmupを120 frameへ揃え、比較元と候補で同じハーネスDLLを使用しました。
1,000要素のうち最後の1要素を変更する通常ケースは6,000 frame、追加負荷は各1,200 frameです。
比較元は120 frameです。短い比較元のp99や最大値を候補の長期保証との比較には使いません。

| 1,000要素の処理 | frame数 | 平均Hz | p95 ms | p99 ms | 最大 ms | 平均割当 MB/frame |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 公開済み版・1要素の色変更 | 120 | 6.59 | 175.76 | 186.18 | 190.14 | 40.51 |
| 最終版・1要素の色変更 | 6,000 | 120.00 | 11.93 | 13.06 | 19.63 | 0.302 |
| 最終版・全要素の色変更 | 1,200 | 119.00 | 11.15 | 12.75 | 28.77 | 2.523 |
| 最終版・全要素の幅と色変更 | 1,200 | 119.30 | 10.66 | 12.55 | 28.74 | 2.623 |
| 最終版・静止したまま提示 | 1,200 | 120.11 | 11.20 | 13.39 | 14.39 | 0.048 |

通常ケースの平均120Hzには到達しました。全要素の更新ではまだ平均120Hzを下回ります。
更新処理の割当と長い完了間隔が残るため、全負荷で8.33msを満たしたとは判断しません。

## 実装

既定の単色FillRectでは、連続する最大1,024矩形を一つのupload、一つのrender pass、一つのinstance drawへまとめます。
境界をまたぐ場合や汎用命令と混在する場合も描画順を保ちます。
回転、DPI、最大8段の矩形clip、premultiplied linear色と元の4×4 coverageを維持します。
軸に平行な矩形の内部ではcoverage判定のbuffer読取と座標変換を減らします。
任意path clip、他のpaint、外部shader、非可逆変換は元の汎用経路を使います。

upload/data bufferとargument tableはRendererの寿命に結び付け、GPU完了・command buffer解放後にPreparedDrawを返した時点で再利用します。
未完了のframe間では共有しません。idleは最大3組・8 MiBで、device loss、準備失敗、上限超過時は破棄します。
解放が失敗したPreparedDrawは未解放batchだけを再試行し、成功済みの解放は繰り返しません。

hostは処理後に毎回固定時間sleepする方式から、固定のframe期限まで待つ方式へ変えました。
dispatcherの継続処理・停止要求で待機を解除し、同じturnで完了を回収します。
描画中のため提出できなかったrootだけで描画期限を消費しません。
長く遅れた場合は期限を再設定し、古いframeを延々と再生しません。
Windows Desktopでは高精度waitable timerとdispatcher eventのいずれかを待ちます。
spinやprocess全体のtimer精度変更は使わず、利用できない場合は通常の待機へ戻します。
Browserにはnative wait handleを作りません。

## 計測と追跡

[ハーネスの手順](../../tools/benchmarks/ui-device/README.md)に、直列presenterと標準application loopの両方を残しています。
applicationの完了callbackとpresenterの段階別callbackは任意の計測用パッチで、製品コードへは含めません。
1要素の色更新、全要素の色更新、全要素の幅と色の更新、静止したままの提示を区別します。
割当はGC.GetTotalAllocatedBytes(false)の全thread差分の近似で、GPU memoryや常駐量ではありません。

全frameは[data/ui-game120](data/ui-game120)に保存し、[summary.json](data/ui-game120/summary.json)は
`tools/benchmarks/ui-device/summarize-application.ps1 -Paths <JSONL一覧>`で再生成できます。
p50/p95/p99はnearest-rankです。有限の測定区間の分位点であり、長期の保証値ではありません。
最終比較と初期runはファイル名で区別します。最終版は`final-*`、途中の再測定は`optimized-*`です。
[DLLのSHA-256](data/ui-game120/binary-manifest.json)と[sourceのSHA-256](data/ui-game120/source-manifest.json)も保存しています。
sourceのhashはcheckout時の改行差を除くためUTF-8/LFへ正規化して計算しています。
baseline/finalのハーネスDLLは同一hashです。製品DLLには完了通知パッチだけを加え、段階別の計測callbackは外して測りました。

段階別の短い診断では、矩形batchと描画期限の改善後でも通常の待機で平均89.94Hzでした。
高精度待機を加えると119.99Hzとなりました。診断runではprepare平均0.87ms、record 0.13ms、submit/present 0.09ms、drain 1.17msでした。
これらはcallbackを付けた120/240 frameの切り分けで、最終版の長時間測定とは区別しています。
前回の約66ms、DPI補正後の約183msという汎用shader側の待ちと、今回のhost待機によるframe間隔の問題は異なる段階です。

## 検証

- Wgpu・Vulkanで、既定batchと外部shaderを使う元の評価経路のRGBA16Float全画素が一致。
  DPI 1/1.5/2、fractional座標、回転、矩形clip、任意path clipへのfallback、半透明重なり、1,024個を超えるbatch境界を含みます。
- GPU完了前の準備間でbufferを共有しないこと、idle最大3組、Rendererを先に閉じた場合の資源保持、解放失敗の再試行を検証。
- 1/100/1,000要素の静止・色更新6ケースで、実Windowの期待画素をassertし、比較元と最終版の全RGBAのSHA-256が一致。
- 最終スタックのGPUテスト107件を直列fixtureで2回成功。Drawing2D単体103件、Application 80件が成功。
- 担当PR単独でも、#79のGPU107件・Drawing2D101件、#89のApplication42件が成功。
- Browser applicationのRelease build成功。Browser/Direct3D12の実機画素と持続FPSは今回の対象外です。
- Markdown 270ファイルのlintと差分チェックが成功。計測パッチは未適用の製品ソースへ適用可能なことを確認。

GPU fixtureの並行実行では、新版と保存済みの旧103件バイナリの双方で複数テストの停止を再現しました。
並行数4でも再発したため、このGPU共有テストassemblyはfixture間を直列にしました。
ケースは削除せず、キャッシュの明示的な並行workerによる検証も維持しています。
停止の内部原因は未特定です。直列検証の成功を、多数の独立native deviceを同時作成する環境の安定性保証とは扱いません。

## 適用範囲と残る課題

この改善は単色矩形を主とするUIでの結果です。文字、画像、複雑なpath、gradient、effectを大量に更新するUIの120Hzを保証しません。
全1,000要素の更新では受動的Widgetの再生成とSignal処理による割当が残ります。
平均が約120Hzでも全frameが8.33ms以内という意味ではなく、OS・GC・GPU通知による長い完了間隔も記録しています。
ゲーム本体の描画と同時に動かす場合は、そのゲームのGPU予算・実際のHUD・対象機種で追加計測が必要です。
