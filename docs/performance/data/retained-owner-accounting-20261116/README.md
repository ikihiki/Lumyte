# retained owner 集計の辞書再利用

## 基準と再現

Production baseline は main の b9c93f001f960221dc13eed5f6edc0bf04914c02、candidate production / frozen harness は 149ab21b9237ed71cbdbd811d63947b495c0b83d です。基準へ同じ benchmark source をコピーしました。先行 clip winding / sparse storage は merged 済みで、既存 PR の変更・merge 操作は行いません。

既存 BDN 0.15.8 / Dictionary を再利用し、新しい外部依存、serializer、記録形式、pool backing、clip 算法、Browser CI の変更はありません。run-abba.sh は build・実行を直列化し、**各 checkout の root へ移動してから**起動します。絶対 project path だけでは生成 project の選択を保証できません。

~~~sh
bash docs/performance/data/retained-owner-accounting-20261116/run-abba.sh
node docs/performance/data/retained-owner-accounting-20261116/summarize.mjs artifacts/owner-accounting/final
~~~

## 公開後の main 同期

公開時に main が c025240ae94b2b9538eec6e344897d5f9fd22a85 へ進み、旧 runner が撤去されました。main を通常 merge し、その削除を尊重しています。PR の active tree に旧 runner を復活させません。production owner 集計の変更は固定測定時と同一です。

run-abba.sh は固定 SHA の専用 worktree を作り、基準へ固定 harness の3ファイルをコピーして当時の実験を再現します。git history に両 SHA が必要です。新 main の標準 benchmark framework を置換するものではなく、保存結果を新 main の再測定結果とも呼びません。

## 測定境界

Headline は四 workload（rect1000 dynamic/cached、path200 dynamic、mixed200 dynamic）×明示的 stats 読取 0/2 回×A1/B1/B2/A2 の **32 completed-frame cases** です。記録・準備・submit・GPU completion wait・command Dispose・retire を含みます。0 回でも library の予算検査は残り、compile/reuse 契約を setup で検証します。2 回は既存 fixture の観測で、全 application の必須処理ではありません。1 回の completed-frame case は未測定です。

別の8 case は同じ fixture の直接 stats 読取です。cached は1 asset、dynamic は64 setup frames 後の26 idle assets（概算65,441,056 bytes）で、A/B の状態一致を log で確認しました。frame、cold 構築、GPU 時間、application FPS の測定とは区別します。

既存 job の6–10 warmup、15–30 actual、100ms target、1% relative-error target、outlier 保持、MemoryDiagnoser を維持します。各 process の中央値・p95・CI・allocation と両 order の比較は summary.json に保存します。p95 は **BDN iteration estimate** の百分位で、個別 frame latency p95 ではありません。数値の判断と全 control の増減は PR 本文にまとめます。

32 frame case はすべて1%精度条件を未達です。最大 iteration 到達、短い実 iteration、outlier を残し、良い結果が出るまで再実行しません。frame の点推定を保証された速度向上や一般的な FPS 改善として扱いません。直接読取は8 case 中3 case だけ精度条件を満たしました。

## 所有とメモリー

毎回の owner/page/pool 再読取、identity deduplication、first contribution、active inclusion、飽和和、既存 eviction/retirement 境界を維持し、合計値や final pack 前の estimate は memoize しません。辞書は観測中だけ貸し出し、再入観測には別辞書を渡し、例外時も clear します。辞書配列の最大容量は生存中 cache に残り得ますが、資産消失時と Dispose 後は再保持しません。

scratch 容量は資産 CachedBytes の概算外です。managed allocation bytes/op と、保持 managed heap、native buffer bytes、実 VRAM は別物で、後3者の総常駐量を測定した主張はしません。AccountMemory の走査回数自体は減らしていません。unit test は GC byte budget / wall threshold を使わず、辞書 identity・空状態・最新値・再入例外・exact page boundary・決定的 operation trace を検証します。

## 証跡と検証範囲

- frames-*.json / reads-*.json: full BDN JSON。末尾空白だけ除去し、元と公開後の SHA-256 は raw-hashes.json に保存しました。数値 token・測定行は不変です。
- `*-harness.sha256` / `*-source.sha256`: source freeze。
- `*-binaries-before/after.sha256`: launcher directory の executable/library files。variant 内 before/after 一致。生成 project の参照先も各 variant root を確認しました。全 generated child executable の process ごとの hash は未収集で、launcher hash と区別します。
- provenance.json: 環境、テスト、先行 PR 状態、除外した pilot、制約。

完全な NativeGraphics suite は実行していません。既知の depth/presentation 失敗・skip を解決したとは主張せず、Radeon Vulkan/wgpu の関連描画・retention/pool と UI host の範囲を記録します。historical profile を現在の frame CPU 比率として流用していません。
