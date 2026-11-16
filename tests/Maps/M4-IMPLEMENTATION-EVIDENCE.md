# M4 空間ストリーミングの検証記録

## 実装と範囲

所有 checkout は `/home/hideaki/.cache/lumyte-maps-m4`、branch は `feat/maps-m4-streaming`。M3 の `ef75c5a4fe056b9da5dfd4a9d23318ef3929dd4b` から開始し、core を `e492d3cfed84e0aea954c8503628d66abc054cf6` に分離した。公開済み M3 `c2042a442bcfa47d04fa1cad7ea36acc17c06ffe` を通常 merge した head は `f70449259c9617f988427d778dd93c045850dfbe`。M3 checkout／branch は変更していない。

4 引数の MapSession が動的 XYZ 需要を計算し、5 引数の fixed／finite を保つ。可視→同内容の祖先→先読みを、距離と正規化 ID で安定選択する。可視数は反復前に検査し、MaxPending 超過は全体を背景にする。未配送要求を取消し、配送済み retain は取消さず終端まで予約を保持する。実行中、未採用完了、cache、退役、maintenance を計上し、LRU では不要な未固定 data だけを返す。親 fallback は子の ownership cell に分割し、準備済み子の下へ半透明の親を重ねない。

新しい ResourceManager／汎用 executor／thread／Task.Run は追加しない。Resources.Execution の共有 runtime と既存の Manager.TrimAsync を使う。HTTP、M5 の source seam、M6 の benchmark は変更せず、M1 の protobuf-net.Core decoder と profile identity／byte guard はそのままである。公開 API と容量・停止契約は [Maps README](../../src/Maps/Lumyte.Maps/README.md) に記載し、ADR 編集は親の担当とする。

## 独立レビューでの修正

最初のレビューで、TrimAsync 成功を個別 tile の解放 acknowledgment と誤認していた問題を検出した。Manager の build view は published snapshot 全体を pin するため、別の配送済み retain が未完了なら trim が 0 解放で成功することがある。これを無視して返金すると、session counter だけが減って unused cache が残る。

修正後は、同じ Manager を借りる全 MapSession の retain を実終端回収まで数える内部 maintenance barrier を共有する。退役が必要なら新規 Maps admission を止め、既配送の全 Maps retain が回収されてから trim する。後から加わった退役は別 batch として保持し、cleanup 失敗は charge と再試行状態を残す。他 session の live pin は返さず、そこへ全額計上する。Maps 以外の外部 snapshot／build view、source-owned cache、host snapshot／GPU 準備領域は別予算であり、process 全体の物理メモリ上限を主張しない。

follow-up の独立レビューで barrier の admission／trim race、counter、通知の lost wakeup に追加 blocker は見つからなかった。一方、fixed／finite の最終 Dispose が未使用 generation を回収しない旧挙動が指摘された。この最終 owner の経路も同じ非同期 cleanup へ統一し、fixed／finite の通常停止・描画規則を変えずに、最終解放・失敗時の再試行まで回収する。新たな fixed／finite 共有 pin と in-flight の試験を追加した。

## 決定的な検証

core 時点で session 35、Maps UI 5、MVT/model 33 が Release で成功し、失敗／skip は 0。session 内の 5 GPU 試験は `LUMYTE_MAPS_GPU_TESTS=1` と Radeon ICD を指定して実行した。

- real Cooperative ResourceRuntime で 32 回の tile build、停止前の 30 回の実 Stage.Release、停止 drain 後の全 32 回の解放を要求する。build-minus-release は移動中も 2 以下で、session counter の減少だけを合格にしない。
- 同じ Manager の別 session が build view を持つ間は退役 charge を保持し、新規 admission を止める。受け付け済み build は取消さず、両 owner を進めてから実解放と次の配送を観測する。
- trim 開始後の内容版切替で増えた退役を、前の batch の完了だけで返金しない。
- 逆順完了、短い同時数／完了枠、旧 camera の結果再利用、最新内容版と既知入力 revision、queue-full の待機復帰、cleanup throw-once と Dispose 再試行を検査する。
- GPU の追加 oracle は親だけ→子の一部→全子の順で、source zoom 1 の親と zoom 2 の 4 子を使う。alpha 0.5 の期待 blue 187～189 と直接／保持型の全画素一致を、縦横の境界内側で確認する。元の M2 seam oracle は変更しない。

Native の既存 sample は zoom 0～2 の手作り local 21 ID を登録し、headless 512×256 で zoom 2 の 16 中心を巡回・再訪する。実採用、pending／retain／maintenance の終端と Manager の実解放を要求してから停止する。source の21 ID・入力ファイル、host 記録は session tile 予算とは別である。これは実都市データや一般的な MVT 互換性、性能結果ではない。

## 再現環境と raw log

共有 SDK 10.0.403／Slang 2026.19 を `LUMYTE_ENV_ROOT=/home/hideaki/.openclaw/workspace/Lumyte/artifacts/dev-env` と checkout の `tools/setup/environment.sh` で使う。Native Rust は既にインストール済みの 1.99.1 を CARGO_HOME／RUSTUP_HOME で選択する。gate、timeout、解析画素の許容値を無効化・拡張せず、package／tool reset は行わない。

実行コマンドは Git 外の `/tmp/lumyte-m4-final-gates.sh`、raw log は `/tmp/lumyte-m4-logs/` に保持する。core の証拠は `core-final-{session,ui,mvt}.log`、厳密な32／30／32の Cooperative 結果は `core-exact-cache-counts.log`。旧失敗は `spatial-suite-first.log`（fallback を予約圧力の下で早く返しすぎた失敗）と `late-batch-*.log`（build-view pin の診断）に残す。

Browser の既知の CDP deadline／cleanup 例外を合格に読み替えない。Native headless は GPU presentation の証拠ではなく、GPU oracle は別の実 Vulkan readback 試験である。計測結果や artifact の公開は行っていない。

## 最終 gate（公開 M3 merge 後）

最終 Release は session **39**（実 Radeon Vulkan GPU **5** を含む）、Maps UI **5**、MVT/model **33**、catalog **20**、Browser windowing JavaScript **9** が成功し、失敗／skip は 0。fixed／finite の最終 owner と混在 in-flight をそれぞれ 2 case の theory で検証し、旧 mode を明示的に維持した。

既存 launcher の headless、strict NativeAOT publish／生成物の実行、strict linux-x64 trim publish／生成物の実行が、すべて実 tile arrival・Manager reclamation・停止まで成功した。strict flag は `ILLinkTreatWarningsAsErrors=true` と NativeAOT の `IlcTreatWarningsAsErrors=true`。Markdown は 340 files／0 issues、`git diff --check` も成功した。

raw log は `final-summary.log`、`final-{session,ui,mvt,catalog,browser-js,launcher}.log`、`final-aot-{publish,run}.log`、`final-trim-{publish,run}.log`、`final-markdown.log`。Browser の全体実行はこの M4 gate で再試行・合格扱いにしていない。継承した M3 の既知の CDP／終了例外と、今回成功した JavaScript 契約試験は別の結果である。
