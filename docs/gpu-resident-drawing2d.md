# GPU 常駐 Drawing2D の更新と寿命

基準: `96688d437e990c0b4f4c5b6cd6cb4a20bbe9a7d9`（2026-11-17）。

## 利用者の変更前・変更後

CPU の DrawingScene2D が編集・階層・dirty の正本であり、新しい UI tree は作らない。
既存の PrepareAsync(snapshot/lease) と、AttachSubmission、完了待ち、command Dispose、Retire の順序を使用する。
従来の exclusive cache と容量境界の互換性を保つため、resident instance は明示的に有効にする。
新しい renderer backend や IRenderer2D の必須メンバーは追加しない。

変更前は既定の DrawingRetentionOptions2D を使う。変更後は次のように同じ renderer と同じ PrepareAsync を使用する。

```csharp
using var renderer = Renderer2D.CreateDefault(device,
    retentionOptions: new() { EnableResidentInstances = true });
using PreparedDrawingFrame2D frame = await renderer.PrepareAsync(snapshot, target);
// target の初期 barrier、frame.Record(commands)、Finish、Submit は従来どおり。
frame.AttachSubmission(submission);
await submission.WaitAsync();
commands.Dispose();
frame.Retire();
```

legacy Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), retentionOptions: ...) も同じ option を受け取る。
標準経路の変更は有効化後だけに限定し、既存の 5/6 引数 constructor、custom shader、custom renderer の契約を維持する。
公開 API の追加は DrawingRetentionOptions2D.EnableResidentInstances（既定 false）だけである。

## 実装境界

- IMPLEMENTED: 完了済み builtin record run の所有者を参照カウントし、同じ native buffer、opaque reference と argument table generation 全体を複数 frame から共有する。
- IMPLEMENTED: 同じ frame 内の複数 placement は初回も一つの producer を共有する。alias は同じ command buffer の先行 producer が成功したことを検証する。これは residency の公開ではない。
- IMPLEMENTED: 他の未完了 frame から未初期化 asset を共有しない。Record では residency を公開せず、既存 retirement の confirmed completion でのみ公開する。
- IMPLEMENTED: solid analytic primitive、fill path、stroke path、line の厳密な整数平行移動は immutable geometry/paint を共有し、既存 64-byte root binding の予約領域の 8 byte に device translation を渡す。vertex bounds と全 16 sample の評価位置が同じ property を GPU で消費する。
- PROPOSED: fractional translation、非 identity の command transform、clip または複雑な paint を含む配置での geometry/paint 部分共有。現実装では従来の依存 run 再構築となる。

Snapshot.Revision や PropertyRevision だけを比較せず、解決済み Content identity、Transform、Clips、target と DPR を照合する。
entry の Node（owner/slot/generation）と ContentRevision/PropertyRevision を frame の instance に保持する。
従って host が revision=0 の snapshot に外側の placement を合成しても、property の実値変更を取りこぼさない。
node slot の再利用は新 generation の instance となる。同じ immutable content の GPU payload を共有することと node identity は別である。
frame には frozen entry から得た値だけを保存し、live scene borrow や UI object を捕捉しない。

## 数値契約と無効化

整数移動の資格は DPR=1、target origin=0、継承・command clip なし、command transform=identity、solid paint、canonical bounds が target 内に収まることに限定する。
target extent と整数移動量をそれぞれ 8192 以下に制限し、pixel origin と 4×4 sample offset の加減算を float32 で厳密に表せる範囲に留める。

DrawingProgram2D.Path/Stroke と PathFlattening2D は local coordinates の geometry を保存し、flatten tolerance と stroke expansion は線形変換の scale に依存する。
identity linear transform に整数 translation を追加しても local geometry、subdivision、paint 値は変わらない。
直接描画の inverse は identity linear part と負の整数 translation であり、sample coordinate から translation を引く評価と一致する。
既存の stroke FMA predicate、mesh coverage、全 16 sample の相関、painter order は変更しない。

canonical quad を target の縁で切り落としたまま再配置しない。既存 DrawingBounds2D の conservative local bounds と 1 pixel の余白が target 内に収まる場合だけ資格を与える。
Standard の device-coordinate membership は integer translation によって sample grid の位相が変わらないため、同じ canonical pixel index の 16-bit sample mask を読む。
coverage の平均値同士を乗算したり、mask を再サンプリングしたりしない。

content replacement はその node の run を新規構築し、変更しない node の GPU identity を保持する。
insert/remove/order/hide/show は frame の run 配列を更新し、painter order を保存する。
ancestor transform/clip は解決済み property の変更として照合する。
scale、DPR、clip、複雑な paint、範囲外の幾何は従来の数値契約のまま依存 run を再構築する。
外部 texture は従来どおり caller borrow であり、同じ view の新内容を次の raster で読む。raster image cache は作らない。

## 所有権と予算

共有単位は転送 byte 列ではなく prepared run 全体であり、record、numeric、paint、metadata、membership、registration、table generation、resource lease を一緒に保持する。
古い table の opaque reference を新 table へ流用しない。新しい frame は元の table generation を保持したまま使用する。
初期化完了後は共有 payload に書き込まない。membership producer は初期化 frame だけが実行し、同じ frame 内の alias も再実行しない。
変更版は新 run を作り、古い frame の buffer を上書きしない。
各 frame は独立した target、command buffer、submission を持ち、旧版と新版が同時に存在できる。

最後の lease を返すまで underlying run を破棄せず、cleanup 失敗時は既存 retry owner に保持する。
failed/unknown submission は従来どおり quarantine し、generation retirement では residency を公開しない。
scene/renderer Dispose は live GPU frame の所有者を解放しない。
成功後の Dispose は冪等であり、既存どおり二度目の明示 Retire は ObjectDisposedException となる。

ledger は共有 underlying owner を一度だけ数え、idle と active を区別する。
追加した lease の 128 byte と共有 owner の 64 byte も保守的に charge する。
既存 MaxWorkingBytes、MaxCachedBytes、MaxCachedAssets、MaxFramesInFlight と backend の実 alignment/binding/table cap を使用し、上限を引き上げない。
frame-root binding は必要な frame work として retained payload upload と区別する。新 native allocator、GPU arena、常駐 bool だけの別 cache は作らない。

## 比較と対象外

既存 DrawRecordPage2D、DrawingArgumentPlan2D、RetainedOwnerLedger2D を利用し、別 library は導入しない。
Skia picture の記録再生や Vello scene の append と同じものとして editable GPU tree を名付けず、実際に保持する native payload と完了境界を上記に限定する。
custom renderer と explicit custom shader は互換 route に残し、immutable sharing / translation の資格を推測しない。
framebuffer damage、partial redraw、Compute/Sparse/mesh の機能追加は対象外。
全 visible run の record と全 target の raster は残るため、frame 全体の O(dirty) は保証しない。

## 検証と再現

CPU の GpuResidentVersionTests は native allocation count、実際の GPU buffer/reference identity、copy command、property binding、同時 3 version、初回 alias、abort、未完了 producer の非公開を deterministic に検査する。
Exclusive control は同じ test の option だけを false にして 4 failure / 1 pass を確認し、単なる静的 cache では検査を満たさないことを確かめる。

Drawing2DTests.ResidentVersions は Vulkan/native wgpu と legacy/Standard の各組合せで、3 submission をすべて受理してから completion wait へ進む。
各 slot は target/command/submission/prepared owner を独立に持ち、2→0→1 の順に retire する。
重なり alpha、曲線 fill/stroke、fractional clip、hole、order、translation、content replacement の全 pixel を、別 Canvas から新しく PrepareAsync(DrawList2D) する直接 reference と厳密比較する。
ResidentMutations は ancestor transform/clip、remove/reinsert generation、hide/show、scale、DPR、rollback、borrowed texture の内容変更を同じ直接 reference と比較する。

生ログ、TRX、環境、counter と source hash は ignore 対象の artifacts/gpu-residency/ に保存する。
速度向上や GPU 時間の測定値は主張しない。host/Graph/Browser/AOT の採用・全構成確認は、この core の検証とは別である。

通常の SDK/native toolchain の用意後は、次の fixture を実行できる。今回の固定環境用 one-liner は artifacts/gpu-residency/env.sh に保存している。

```bash
source tools/setup/activate.sh
dotnet test tests/Graphics/Lumyte.Graphics.Drawing2D.Tests -c Release --filter GpuResidentVersionTests
LUMYTE_GRAPHICS_GPU_TESTS=1 dotnet test tests/Graphics/Lumyte.Graphics.Tests -c Release --filter 'FullyQualifiedName~ResidentVersions|FullyQualifiedName~ResidentMutations|FullyQualifiedName~ResidentDeviceGenerations'
```
