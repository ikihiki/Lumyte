# 状態保持Drawing2D調査PRの資料について

このPRは提案ADR・設計・benchmark-only prototype・測定資料をまとめる。製品Drawing2D/UIへの保持scene/bulk実装の組込みや、既存PR #105のsnapshot storage最適化を含まない。

## 実装状態

- CPU保持＋平坦化prototype、限定した単色矩形の直接保持/GPU resident slotはbenchmark配下だけに存在する。
- 2D/ゲームUI/エディタUIの12fixtureは既存Canvas/Renderer/UI/Textを使う測定基盤。
- 一般のretained scene/property treeとbulk typed構築は提案ADRであり未実装。
- 12fixtureの正式A/Bはmain/#105で、直接保持方式のpath/text/clip/UI対応を測ったものではない。

## 公開コピーと元データ

測定はレポート記載のlocal source revisionを固定して行った。公開branchはmainから選択した資料/コードをまとめ直したもので、調査中のlocal commit列をそのままpushしていない。元のsource revisionとC# file SHAを測定provenanceへ残す。後のCLI整備/資料編集を、測定時のcodeへ遡って適用したとは扱わない。

公開raw copyではcheckout/home prefix・host名・user名をplaceholderへ置換する。計測の数値、case、iteration、pixel hashは変更しない。変更のあったgzipはmanifestのbytes/sha256が公開コピーを検証し、original_bytes/original_sha256が元データを識別する。gzipはmtime=0で再生成する。
localの元資料はarchive branchと実行artifactへ保持している。公開結果の加工と測定元の削除/選別を混同しない。

元revisionの短縮SHAは測定識別子であり、この公開PRの全ての過去commitがGitHubへ存在する保証ではない。再現には公開harnessとレポートに記載したbaselineの組合せを用い、source SHA・公開manifestと実測値の出典を確認する。

## 設計の正本

- [GRAPHICS-0025: 状態保持描画content](../adr/graphics/GRAPHICS-0025-retained-drawing-content.md)
- [GRAPHICS-0026: bulk更新経路](../adr/graphics/GRAPHICS-0026-bulk-drawing-updates.md)
- [部分更新の詳細設計](../design/drawing2d-incremental-rendering.md)
- [bulkの詳細設計](../design/drawing2d-bulk-path.md)

ADRはいずれも提案状態。製品API採用/実装/mergeは本PR作成とは別の判断である。
