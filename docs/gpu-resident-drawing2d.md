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
core の option は DrawingRetentionOptions2D.EnableResidentInstances（既定 false）であり、
Graph integration は後述の AppendResident2D overload を追加する。

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

content replacement は変更された invalidation chunk を新規構築し、変更しない chunks/nodes の GPU identity を保持する。
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

## アプリケーションと共有 Graph への採用

通常の UiHostPresenter は builtin Standard renderer の EnableResidentInstances を有効にし、
既存 maximumFrames をそのまま MaxFramesInFlight に渡す。
汎用 Renderer2D constructor と custom resource/renderer の既定は false のままである。
candidate、visual capture/update、通常描画は同じ PreparedDrawingFrame2D の
submission/completion/command retirement 契約を使用する。
外側の実 clip は保持するため、一般的な host placement の整数移動まで upload=0 とは約束しない。

共有 RenderGraph は明示的な AppendResident2D を利用できる。
既存の Append2D/IDrawingPassRenderer2D は従来どおり descriptor preparation を行い、
この opt-in を暗黙に適用しない。

- external target を graph.ImportTexture し、その allocation を DrawingGraphBindings2D に BindTexture する。
- 通常の renderer.PrepareAsync(snapshot, target) で frozen frame を準備する。
- graph.AppendResident2D(frame, targetBinding, bindings) を compile より前に一度だけ呼ぶ。
- plan.Record(commands)、Finish、Submit を実行し、実 submission を frame.AttachSubmission に渡す。
- GPU completion を待ち、commands、plan、graph を Dispose してから frame.Retire する。

この API は prepared frame を caller から借り、plan に ownership を移さない。
borrowed image/sampler/target も caller が完了まで保持する。
pass の外部 texture 宣言と binding snapshot は compile 前に固定される。
private copy/compute/raster は一つの pass 内で実行され、必要な初期化だけを consumer から分離して cull しない。
Record、plan.Dispose、graph.Dispose は residency を公開しない。
unexecuted plan は Dispose 後に未記録 frame.Dispose、record 済み never-submitted work は
command Dispose 後に AbortUnsubmitted を使用する。unknown submit は解放せず quarantine する。

現 route の target は既に allocation/view を持つ imported texture であり、外部 write は Graph の
observable root となる。未使用 transient output の culling や late-bound target allocation はこの overload の対象外である。
これは nested graph、GPU tree mirror、新 shader ABI ではない。

## content page と GPU invalidation chunk

EnableResidentInstances=true の retained route は最大 4 canonical CPU pages（最大 256 logical commands）を
一つの immutable GPU invalidation chunk とする。false は従来の whole entry である。
これは「dirty page だけ」の実装ではない。WithPage は含まれる最大 4 pages の chunk 全体を再構築する。
同じ entry のそれ以外の prefix/suffix chunks は元の native payload、registration、table generation を共有する。
初期 192 compatible commands は 64-command の CPU page 境界を跨いで一つの GPU record range に batch する。
個々の command が複数 records に展開される場合、実 record packing、texture/clip/capacity 境界が
GPU allocation/draw を決定し、logical command count を physical record count と取り違えない。

chunk ごとに既存 PreparedRecordBatch2D/ResidentBatchLease2D を使い、別 arena は導入しない。
同じ chunk の再編集は copy-on-write であり、古い frame の ranges を書き換えない。
全 owner は既存 cache/ledger と最後の lease の cleanup に従い、idle asset/byte cap が断片の保持を制限する。
active frame の owner を cap 達成のために破棄しない。単一 oversized payload は既存 binding/working limit により拒否する。
さらに細かい page/record span の再利用や、初期 whole-entry allocation 内の untouched ranges を切り出す方式は未実装である。
frame-visible traversal と全 raster は依然 O(visible) であり、frame 全体の O(dirty) や速度向上は主張しない。

## integration の counter と検証単位

integration の payloadCopyBytes は、実際に発行された CopyBuffer の source storage bytes の和である。
raw buffer は SizeInBytes、shader-data は Count × ShaderElementStrideInBytes を数える。
frame-private root の SetArguments、target 初期化、readback、画面 present の費用はこの値に含めない。
ゼロ byte はそれらの frame work や raster がゼロという意味ではない。
CompiledRuns は GPU invalidation chunks の構築回数であり、CPU pages、physical draw 数、driver VRAM 量ではない。

ResidentIntegrationTests は 640 commands の 3 chunks から中央 chunk だけを変更し、
残る prefix/suffix の native records identity と no-op/dirty copy bytes を比較する。
192 compatible commands の初期 preparation が CPU page 境界を跨いで一つの record range になることも検査する。
ResidentGraph は Vulkan と native wgpu の実 submission/completion 後にだけ
次の frame の同じ native payload/table generation を共有し、fresh direct pixels と比較する。
通常 presenter の native test は no-op の geometry/table identity、payload copy と membership dispatch の不変を検査する。
外側 placement、実 clip、DPR、visibility の pixel reference は revision=0 の hosted source で検査する。

生データ、失敗した controls、TRX、actual source/binary hash と再現 command は
artifacts/gpu-residency/integration/ に保存する。cached Shinjuku は既存の環境 opt-in fixture だけを使用し、
MVT 本体を test source や repository へ追加しない。whole-map の page update 検証は逐次完了であり、
3 simultaneous submitted revisions の証明は独立 target/command/submission の native ResidentVersions fixture で行う。
Browser SwiftShader の検査は機能検証であって hardware 性能の根拠ではない。

WgpuHostedRevisionZeroMutations は実 UiHostedDrawingSource と UiHostPresenter を通し、
no-op、signal による一つの leaf の変更、外側 clip/scale/placement、hide/show と、
presentation/UiSurface Dispose 後の古い captured snapshot を native GPU で検査する。
全画素を別の新規 legacy direct renderer と比較し、変更しない sibling の native buffer/table identity を保持する。
外側 clip を別の矩形に変更して元へ戻す場合、host は新しい immutable Path2D を構築するため、
現 dependency-keyed route は同じ形状であっても依存 chunks を再構築する。
この case の copy bytes も保存し、visibility 復帰やすべての property 更新を常に zero-upload とは呼ばない。
