# 用途別レンダリングworkload suite

2026-11-02、2Dコンテンツ・ゲームUI・エディタUIを独立選択できるbenchmarkを追加した。
[全case・引数・測定scope](../../tools/benchmarks/drawing2d-incremental/WORKLOADS.md)を正本とする。
fixture実装は `46d1907`。製品APIの変更はなく、通常のCanvas/Renderer/UIを使う。

正式設定でのmain/#105比較は[12ワークロード正式比較](formal-rendering-workload-comparison.md)へ保存した。以下のquick/smoke記録とは区別する。

## 収録ケース

- 2d（4）: static、全instance transform、geometry stream、camera pan。
- game-ui（3）: 静止HUD＋動く背景、HUDのSignal値更新、menu layout animation。
- editor-ui（5）: idle、単一行TextBox編集、可視行scroll、dock風resize、動く2D preview。

UIは実UiSurface/Element、文字は実Parley/TextBoxを使う。static recording cacheと可視行virtualizationはbench側の明示policy。汎用製品機能・完全なeditor/ゲームengine・IME/3D性能の評価とは区別する。

## 個別実行

repo rootで先にbuildする。

```bash
bash tools/benchmarks/drawing2d-incremental/run-workloads.sh build
bash tools/benchmarks/drawing2d-incremental/run-workloads.sh list
bash tools/benchmarks/drawing2d-incremental/run-workloads.sh cpu --suite 2d --count 1000
bash tools/benchmarks/drawing2d-incremental/run-workloads.sh cpu --suite game-ui --count 200
bash tools/benchmarks/drawing2d-incremental/run-workloads.sh cpu --suite editor-ui --count 200
```

`--case editor-line-edit`等で1 caseだけ選べる。
`hardware`はCPU工程別とGPU timestamp、`--mode pipeline --frames-in-flight 3`は最終drain込みのheadless throughputを測る。

## 動作検証済みの範囲

- Release build: 0 warning/error。
- 12 caseすべてでCPU smoke 5 frame。
- BDN quick job: 12 caseすべてで3 measured iteration、非null Statistics/正のTotalOperations。
- Radeon8060S/RADV: count32の工程別各2sample、pipeline depth1/3各4frameを全12 caseで実行。
- depth1/3の対応checksum一致、query数とfinal drainを確認。
- default count200も全12 caseで実GPU readbackを確認。
- 未知suite/case、重複option、不正sample数、stagesと複数frame設定の組合せを拒否。

短いquick/smokeは起動と配線の確認用であり、用途の性能順位や最適化効果を示す測定ではない。
GPU readbackの確認は非空画素とchecksumであり、独立したgolden画像の正解検証ではない。

[検証metadata](data/rendering-workloads-verification-20261102/verification.json)、[raw gzip/SHA-256](data/rendering-workloads-verification-20261102/manifest.json)を保存した。
各caseの十分なwarmup/独立process反復による正式なbefore/after比較は、同じcase/scale/targetで別途行う。
