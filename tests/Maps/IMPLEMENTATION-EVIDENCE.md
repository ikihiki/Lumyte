# M1/M2 implementation evidence

Latest status is the final prerequisite section below; earlier failure sections preserve the investigation
and are superseded only where the later source and tests explicitly establish the remedy.

Owned checkout: /home/hideaki/.cache/lumyte-maps-implementation.
M1 branch feat/maps-m1-local-mvt, commit 7b6e89879b7f8dddee19d9b03c40f870ae4b42c7.
M2 branch feat/maps-m2-fixed-ui starts from that exact commit.
The parent subsequently integrated the adopted ownership-clip ADR and cherry-picked the isolated parser
fix onto M1; final M1 is 0192d777a08db86a477a9cdc6a438eed7551a4d4. Its independent rebuilt Release
suite passes 17/17 (zero skips), /tmp/maps-parent-m1-final.log. M2 includes that exact downstack head.
Historical failures and interim counts below are retained as provenance; final results supersede them.

## Environment

SDK 10.0.403 from shared Lumyte dev-env, Release, unmodified analyzer/warnings gates.
LUMYTE_ENV_ROOT=/home/hideaki/.openclaw/workspace/Lumyte/artifacts/dev-env;
source tools/setup/environment.sh; prepend mise/data/installs/http-dotnet/10.0.403 and
mise/data/installs/http-slang/2026.19/bin to PATH. No tooling installation/reset or CI edits.
Actual GPU: Radeon 8060S Graphics (RADV STRIX_HALO), Mesa 26.0.8, Vulkan 1.4.335,
verified with shared sysroot vulkaninfo and /usr/share/vulkan/icd.d/radeon_icd.json.
Hardware identity raw log: /tmp/maps-vulkan-info.log.

## Executed gates

- Isolated M1 Release: 16 passed, zero skipped, /tmp/maps-m1-isolated.log.
- Integrated MVT Release: /tmp/maps-final-mvt.log (16 passed, zero skipped).
- UI record-only mount/reuse/replacement: /tmp/maps-final-ui.log.
- Session lifetime/admission plus actual hardware analytic polygon-hole/road pixels and direct versus
  MapView retained readback equality: /tmp/maps-final-session-gpu.log; earlier full 7-case hardware
  pass /tmp/maps-session-complete.log, parity /tmp/maps-retained-gpu.log.
- Real sample catalog tests: /tmp/maps-final-catalog.log; prior 17-case pass /tmp/maps-catalog-tests.log.
- Actual Release launcher: /tmp/maps-final-launcher.log. Debug launcher also exercised in
  /tmp/maps-launcher-run.log. Direct Release adoption smoke: /tmp/maps-adoption-smoke.log.
- Strict NativeAOT: /tmp/maps-aot-adoption-publish.log and /tmp/maps-aot-adoption-run.log;
  flags PublishAot=true, ILLinkTreatWarningsAsErrors=true, IlcTreatWarningsAsErrors=true.
- Strict trimmed: /tmp/maps-trim-adoption-publish.log and /tmp/maps-trim-adoption-run.log;
  PublishTrimmed=true, self-contained linux-x64, ILLinkTreatWarningsAsErrors=true.
- Markdown lint: /tmp/maps-markdown.log. Diff check: /tmp/maps-final-diffcheck.log.

Raw actual hardware RGBA readback: /tmp/lumyte-map-gpu-rgba.bin; PNG conversion
/tmp/lumyte-map-gpu.png (64 x 64), SHA256 ffbc90d4efc5b50d4f9d71e5a923d5af627b81b1b201b00fc442da86d7bb669b.
These artifacts are outside Git; no generated binary was added.

## Historical handoff gaps (superseded by final verification below)

The implemented native vertical slices are usable, but the complete requested M2 acceptance is not
claimed: Browser execution and the broader DPI/parent-clip/overlay/edge GPU matrix remain incomplete.
These are explicit handoff gaps, not passing or silently skipped checks.

## Failures retained and limits

Initial strict analyzer failures and the incorrect test assumption that Rebuild adopts immediately are
retained in /tmp/maps-ui-tests-complete.log and earlier build logs. The regression was corrected to
exercise UpdateTree, the actual deferred publication boundary. No production gate was weakened.
One concurrent MSBuild worker-node resolver failure is retained in /tmp/maps-launcher-release.log;
final gates serialize builds with node reuse disabled, without resetting shared tooling.

Browser execution is blocked for this local-file-only adapter/sample: no Browser input/source or
browser map sample is implemented. Installed wasm-tools alone is not a compatibility proof.
The native AOT smoke verifies only the handmade fixture, not alternate CLR layout accounting.
No seam-free real-city claim, HTTP, M3 interaction/selection, parent fallback or performance claim.
Mixed zoom fixed sets are explicitly rejected. Broad DPI, parent clip/overlay and semitransparent
mixed-extent tile-edge GPU oracles remain incomplete; this evidence does not call those passed.
Decoder validates winding/groups, not self-intersections or geometric hole containment.

## M2 followup verification

A bounded immutable in-memory MVT registration/source and portable stage path now support browser
inputs without filesystem or HTTP. Actual Release trimmed browser execution passed with the existing
application Resources cooperative runtime: adoption, five mounted MapView commands, unchanged retained
reuse and orderly stop. Raw logs and command/environment manifest: artifacts/maps-m2-followup/.
This supersedes the earlier "no browser source" implementation gap. It is a recording/adoption proof,
not Browser GPU pixel proof. Optional Browser GPU capture failed with automation/device recovery timeout;
the strict capture path and failure logs are retained, with no tolerance or deadline relaxation.

Expanded actual Radeon hardware matrix passed at integer tile boundaries and DPI 1/2, extents 1024/8192,
alpha-0.5 buffered polygons, casing/interior roads, overlay order and a nonvacuous overflowing child clipped
by its actual UI parent. Full direct/retained readbacks match; 11 tests passed /tmp/maps-expanded-gpu5.log.
The standard existing LUMYTE_GRAPHICS_GPU_TESTS=1 enables these tests, with the prior map flag as an alias.
Canonical slnx discovery through build Test includes Maps projects without CI gate edits.

**Historical failing hardware acceptance (fixed below):** at a half-logical-pixel ownership boundary x=191.5, adjacent tile
clip coverages compose independently. Expected flat blue sRGB188 is actual177 at the shared edge.
Direct/retained parity still holds, so it does not detect this renderer-level compositing error.
The analytic oracle is retained unchanged in MapVisualTests; the suite at that pre-fix snapshot was failing (1 failed,
10 passed, zero skipped), /tmp/maps-fractional-edge-gpu.log. No tolerance widening, boundary snapping,
or test removal was applied. Raw /tmp/lumyte-map-dpi-1-0.17578125.rgba. A public Canvas sample-coherent
ownership clipping/compositing facility is not present; modifying generic Graphics production is outside
the assigned write scope, and parent authorization has been requested before choosing that remedy.

Followup session tests cover terminal malformed build failures without auto-retry, uncollected success
at stop without publication, repeated BeginStop, rejecting changes after stop, callback failure without
repeat notification and manifest zoom rejection. Quadratic duplicate attribute validation is replaced by
a charged scratch name array and ordinal sort; a 2048-attribute unique/duplicate-alias regression passes.
No existing commits were amended and no branch was published.

## Final narrow Graphics prerequisite and review fixes

- M1-only parser fix commit: 84380786d2c5993d0389753596ebbbd4577c11a1; only decoder and its test file.
  Parent may cherry-pick onto the isolated M1 branch without any Browser/UI dependency.
- Scoped unmerged ADR amendment: 97b1b99bd95af7320e71722c12afe870cb7360d1; distinguishes ordinary
  UI antialias coverage from half-open device-center ownership, adds before/after usage and API, explicitly
  rejects accumulated rotation/shear. Parent owns integration into the unmerged design branch.
- Generic prerequisite: 727f612b6722d9683b135424dde92ef5fadf93ec; ClipOwnershipRect plus ownership
  metadata, normalization and cache identity, generic CPU and actual hardware GPU regression fixtures.
  Ordinary clips keep their original coverage semantics. Geometry/camera positions are never snapped.

The original fractional seam oracle is preserved: before generic ownership clipping the expected188
actual177 failure is artifacts/maps-m2-followup/fractional-edge-before.log. After applying the new clip
only at tile ownership boundaries, Maps hardware suite passes13/13 with zero skips, including both
integer/half-pixel tile positions and DPI1/1.5/2, extents1024/8192, alpha0.5 polygons and road casing,
actual parent-overflow clip/overlay and entire direct/retained readback equality.

Generic full Drawing2D unit suite passes405/405. Existing Drawing2D hardware GPU subset passes37/37
(includes new ownership regression across translation/reflection, scale1/1.5/2, FillRect/FillPath,
fractional outer antialias and opacity). Replay/retained transformation composition is tested;
rotation/shear explicitly throws NotSupportedException, not a silent antialiased fallback. Empty and
singular ownership rectangles own no pixels. No generic arbitrary-transform/no-seams claim is made.

Both recording review regressions fail against original d4b3d276 Record implementation (two failures,
zero passes), artifacts/maps-m2-followup/review-recording-before.log. Fixed recording shares visibility
and type eligibility with preflight; dense offscreen and paint-ineligible features no longer reject a
small visible map at4096-byte conversion budget. Dense visible data still rejects and zero viewport
records nothing. Loaded normalized X tiles repeat near both +179/-179 and across multiple visible worlds
without additional resource builds or any M3 selection. Repetition counts are bounded before recording.

Final complete Release commands and raw logs: /tmp/maps-final-prerequisite-gates.sh,
/tmp/maps-prereq-final-*.log (also copied into artifacts/maps-m2-followup after completion). Counts:
MVT/memory21, UI2, session+actual Radeon hardware13, catalog18; all zero skips. Strict NativeAOT and
trimmed linux-x64 samples wait for actual adoption then stop. Strict trimmed Browser sample uses its
stock Resources cooperative runtime and verifies real adoption, mounted map recording (13 commands with visible world repetitions),
unchanged retained reuse and orderly cleanup. Optional Browser GPU screenshot remains an explicitly
unverified automation/device-recovery failure, not a substitute for native hardware pixel evidence.

The real markdownlint executable (markdownlint-cli2-bin.mjs) validates331files with0issues. Earlier
invocations of its library module produced no banner and were not a valid lint gate; only the later
banner/count-bearing results are evidence. No CI gate, timeout, analytic oracle or tool installation was
weakened or reset. No commits are amended and nothing is published.

Final Browser rerun exposed an exported async StopAsync promise surviving the awaited runMain shutdown;
its "valid external Instance reference" failure is preserved in /tmp/maps-prereq-final-browser-audited.log.
The smoke now exports synchronous owner-queued RequestStop and independently waits for the JavaScript
runMain completion/error flags, avoiding an external promise lifetime across runtime teardown. The
strictly trimmed final source passes with13 mounted commands, retained reuse, audited SwiftShader and
orderly stop: /tmp/maps-prereq-browser-stopfix-publish.log and /tmp/maps-prereq-browser-stopfix-run.log.
No generic host lifetime behavior or shutdown timeout was changed to make this sample pass.

## protobuf-net.Core方式への移行後の検証

protobufのwire読取りをApache-2.0の`protobuf-net.Core 3.4.40`の公開APIへ委譲し、手書きの`ProtoReader.cs`を削除した。MVTの意味検査、形状復元、strict UTF-8、確保前の容量検査は維持した。入力はReadOnlyMemoryとbyte配列overloadへ変更し、Local／InMemoryのsnapshotをコピーせず渡す。文字列scratchの実配列量とreader作成／再作成ごとの1024byteを累積計上するが、依存内部とruntime全体のallocation上限の証明ではない。より厳しいprofileでは従来より早く拒否する場合がある。

- M1のRelease Rebuild：警告／エラー0、回帰試験29件成功、失敗／skip0。`/tmp/mvt-release-rebuild.log`、`/tmp/mvt-release-final-tests.log`。
- M2統合後の再build：Maps33件、UI2件、sessionと実Radeon GPU13件、catalog18件が成功。失敗／skip0。`/tmp/migration-Lumyte.Maps.Tests.log`、`/tmp/migration-Lumyte.Maps.UI.Tests.log`、`/tmp/migration-Lumyte.Maps.Session.Tests.log`、`/tmp/migration-catalog.log`。
- 実際のRelease launcherを実行し、実タイル採用を待って正常終了。`/tmp/migration-launcher.log`。
- consumer全体のstrict NativeAOTとnative trim publish、および各成果物の実タイル採用・正常終了が成功。`/tmp/migration-aot-publish.log`、`/tmp/migration-aot-run.log`、`/tmp/migration-trim-publish.log`、`/tmp/migration-trim-run.log`。
- strict Browser publishと、実ResourcesでのMVT採用、mount済みUI記録13命令、保持型記録の再利用は成功。ただし終了処理が`GraphicsDeviceLostException: A valid external Instance reference no longer exists.`で失敗した。Browser全体の検証成功とは扱わない。`/tmp/migration-browser-publish.log`、`/tmp/migration-browser-run.log`。

追加の比較は移行前bd2bb792の隔離worktreeで1回、候補側の診断付き検証で1回だけ実施した。新たなbaseline実行は正常終了し、候補は同じ終了例外を再現した。スタックはBrowserSubmission.WaitAsync→UiHostPresenterのdrain→UiVisualCoordinator→UiApplicationViewSlotの解放を示す。Browserサンプル、Graphics／Platform／UIホスト、検証scriptはbaselineと差分がない。移行前の`/tmp/maps-prereq-final-browser-run-fixed.log`にも同じ例外の履歴があるが、今回のbaselineは成功しているため、移行によるタイミング影響を断定的に除外しない。`/tmp/migration-browser-baseline-publish.log`、`/tmp/migration-browser-baseline-run.log`、`/tmp/migration-browser-candidate-diagnostic.log`を保持し、assertion／期限の変更や成功するまでの反復は行っていない。

独立した静的レビューでは具体的な移行ブロッカーなし。これはsourceと試験の読取りレビューであり、fuzzingや依存内部allocationの実測、Browser全体の合格を意味しない。以前の検証結果は履歴として残し、移行後の評価には本節を用いる。

## M3：有限候補の可視選択と既存 UI 入力（2026-11-16）

実装元は M2 `0174a93f87a4663b29b496a28482b5846dd34a3b`。M2 の所有境界修正を先に分離し、親が取り込んだ最新 M2 `960a18e208a8d0e942dc84c46aa7c21970195a62` を通常 merge して stack の祖先関係を保持した。

- M2 前提修正：Graphics `d35e996c2bf30f669e95d506e3eae2b5c94d0591` と Maps `6e7497dd44eb614a7b21a74c76da24b4646345cf`。zoom 1.4、取得 zoom 1、viewport 512、longitude -0.13321727635345296 の独立した解析画素試験は、修正前に期待 blue 187–189 に対して 225 で失敗し、同じ試験のまま修正後に成功した。隣接端点を整数 tile corner から同じ演算で作り、origin と size を別々に丸めて再加算しない。
- 汎用 UI 前提：`829aadc81f84a3ce0f48034e420b05223781b6d3`。position 付き wheel を既存の hit-test／bubbling／popup 遮蔽へ接続する。Linux／Windows／Browser は位置と delta を同一 acquisition で渡す。実行器や OS イベントの別経路は追加しない。Windows の画面座標は client 原点を引いて既存 scale で一度だけ論理座標へ変換する。
- M3 本体：`e91e2de01215c970ff36874e5608a497c6f86f87`。有限候補モードは opt-in。未知領域は背景、採用済み tile は停止まで保持し、容量不足は待機する。空間優先 queue／LRU／HTTP の実装ではない。
- Linux 前提呼出しの StyleCop 修正：`f9851a3deba951d4dec272226d7a53d1bb95730e`。入力の意味は変えない。

最終の focused Release 検証は UI 全体 405、production Application router 2、Windowing 全体 53、Maps UI 5、Maps session 18、MVT/model 33、sample catalog 20 が成功し、失敗／skip は 0。session の 4 GPU 試験は Radeon Vulkan の実読戻しを有効にしており、record-only を GPU 成功とは数えない。固定経路は 0.5 pixel パン→ポインター基準の約 +0.4 zoom→(-20,3) pixel パン。alpha 0.5 の解析期待色と直接／保持型の全画素一致を確認する。元の分数 zoom seam oracle の許容値は変更していない。

独立した読取りレビューで、production router が captured Move の画面外座標を (-1,-1) へ置換する既存挙動が M3 のパンを壊すと判明した。汎用前提 commit で実座標を維持し、画面外 (130,140)→(160,150)→再進入 (70,60) の regression を追加した。公開 UiSurface の MapView 試験は capture loss／focus loss、overlay control 優先、offset layout の wheel anchor、日付変更線、緯度 clamp を確認する。

SDK は共有 .NET 10.0.403、Slang は 2026.19。環境は `LUMYTE_ENV_ROOT=/home/hideaki/.openclaw/workspace/Lumyte/artifacts/dev-env` と owned checkout の `tools/setup/environment.sh` を使う。Rust の自動 bootstrap は初回に既定の 300 秒 HTTP timeout で失敗したため、既にインストール済みの 1.99.1 を `CARGO_HOME`／`RUSTUP_HOME` で明示して再実行した。gate／timeout を無効化したり package／tool を reset したりしていない。strict NativeAOT publish と生成物の headless 実行は、実 tile の採用を待って正常終了した。Windows project は Release build に成功したが、Windows 上で入力を実行した証拠ではない。

raw log は `/tmp/lumyte-m3-logs/`。修正前 seam は `seam-before.log`、M2 修正後は `m2-after-final.log`、最終 focused は `final-{ui,application,popup,maps-ui,maps-session,mvt,catalog}.log`、追加の不可視終端採用も含む最終 session は `final-maps-session-18.log`、strict AOT は `final-aot-{publish,run}.log`。再現コマンドを記録した `/tmp/lumyte-m3-final-gates.sh` と `/tmp/lumyte-m3-publish-gates.sh` は Git 外の実行記録で、性能結果や公開 artifact ではない。

strict linux-x64 trim publish／生成物実行、既存 launcher の実行も成功した。strict Browser publish は成功したが、Browser 全体は終了処理の既知の未解決条件で失敗した。raw log は `final-trim-{publish,run}.log`、`final-launcher.log`、`browser-presented-{publish,run}.log`。

Browser の入力試験は最初、Resources の採用と手動 UI 記録だけで入力可能と判断したため失敗した。診断で acquisition／drain と production router の選択は成功し、popup manager の提示済み hit-test が null と判明した。既存の公開 `IUiWindowManager.HitTest` で提示済み入力幾何を待つ条件を追加し、元の startup deadline 内で検証した。幾何の acknowledgment を強制したり、未提示領域へ入力を配送したりはしていない。`browser-acquisition-diagnostics.log` と `browser-managed-trace-run.log` は診断専用で、Console trace／artifact JS instrumentation は最終の production source から除去した。

最終の Browser 実行では、実 CDP→DOM→acquisition→production host→MapView の 40×20 pixel パン、+0.25 zoom の地理 anchor、取得 zoom 0→1 による 4 枚の追加採用（総数5）と再記録が成功した。software WebGPU の adapter log は SwiftShader を示す。物理マウス操作や Browser の実 GPU 画素証拠とは呼ばない。その後の停止で `A valid external Instance reference no longer exists.` を含む AggregateException が報告され、cleanup assertion が失敗した。移行後 M2 の履歴にも同系統の終了例外があるが、M3 について Browser 全体の成功とは数えず、期限や assertion を変更して再試行しない。

追加の不可視完了試験では、配送後に需要を移動して viewport を 0 にし、旧 retain が成功した後も採用数1／保持128 byte の全額計上を維持することを確認した。Manager trim は session の pin がある間は解放せず、Dispose 後に1回だけ解放した。未配送の別 tile は取得していない。
