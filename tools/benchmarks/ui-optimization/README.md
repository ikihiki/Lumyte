# UI optimization benchmark

`UiSurface`の部分更新・layout・animation、標準hostのCPU描画frame、shared-element検索を同一ソースで比較します。

```sh
dotnet build tools/benchmarks/ui-optimization -c Release
DOTNET_TieredCompilation=0 DOTNET_gcServer=0 dotnet \
  tools/benchmarks/ui-optimization/bin/Release/net10.0/Lumyte.UI.Application.Tests.dll \
  > results.jsonl
```

`--smoke`は小さいデータで動作確認だけを行います。測定対象を分ける場合は
`--section=allocation`、`--section=animation`、`--section=layout`、`--section=host`、`--section=shared`を使います。
ビルドと別の測定が終わってから実行してください。比較する両checkoutへ同一のbenchmarkディレクトリを置きます。

全項目で5バッチの時間・current-thread managed allocationと中央値をJSON Linesへ出力します。
GCはバッチの間だけ実行し、構築・assertion・JSON出力を計測に含めません。
回数とwarmupは各行へ記録します。時間の統計的信頼区間は算出していません。

- 空の派生Elementは同じ宣言から10,000回構築し、配列に保持して一個当たりの構築割り当てを測ります。Widget・保持配列の構築は計測外です。全Widget型やworking setの測定ではありません。
- 非表示animationは独立producer 100/1,000/5,000個のpendingまたはrunning getterを測ります。停止・Surface停止ではなく、親Panelのみを非表示にします。状態が非表示中も保持され、再表示すれば検出できることを検証します。
- 幅animationは共有producer、1,000 Rectangle、仮想60 Hzです。密な更新は全要素、疎な更新は一つを更新し、AdvanceAnimationsとUpdateLayoutを含めます。
- Signal幅更新は平坦な10,000葉、または100×100葉で一つを変更し、値代入とUpdateLayout（UpdateTreeを含む）を測ります。
- hostは実際のUiApplication.Turnを単一owner threadで手動進行し、同期fake driverへ毎frame提出します。静止・一つの色変更・custom Panelを各1,000/10,000葉で測り、提出回数、DrawList identity再利用、最新2frameの全命令の色・geometry・transform・clipを計測外に確認します。custom対照はrootが派生Panelであり、末尾customの探索コストではありません。
- shared検索は1,000/10,000要素の8 keysに対し、ResolveTargetを3巡＋ResolveTargetsを1回呼びます。Element identityと集合を検証します。frame全体の測定ではありません。

hostを通常の経路で動かすため、内部検証用の既存friend assembly名を使った独立実行ファイルです。
private初期化とターゲット検索のdelegate解決は測定前だけ行います。本体のpublic APIやproduction codeは変更しません。
GPU描画・実画面提示・pixel fill・OS入力・入力から表示までの遅延は測定に含みません。

[変更前後の比較・全batchの生データ](../../../docs/performance/ui-performance-optimizations.md)を保存しています。
