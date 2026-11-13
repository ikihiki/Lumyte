# Shader transfer migration validation

The breaking contract is documented in
[GRAPHICS-0035](adr/graphics/GRAPHICS-0035-csharp-authoritative-shader-transfer.md) and the
[generator README](../src/Graphics/Lumyte.Graphics.Generators/README.md).

## 任意検証レイヤの追補（2026-11-13）

以下は777a7e11後の任意検証レイヤを対象とする最新結果である。
後続の節はそれぞれの変更時点の結果であり、Debug限定の旧検証方針は
明示的なdevice単位の有効化へ置き換わっている。

具体的なバックエンドのdeviceを生成した直後、子リソースを生成する前に
EnableValidation()を呼ぶ。Debug・Releaseとも既定では無効であり、
IGraphicDevice.ValidationEnabledで状態を確認できる。D3D12自身の
EnableDebugLayerとは別の、Lumyteの管理側契約に対する診断である。

- 有効時: 記録済み・実行中リソースの寿命、マッピング競合、親子関係、
  記録時とSubmit時の登録失効、範囲別バッファアクセス、mip/layer別の
  テクスチャ状態、コマンドをまたぐ依存、バリアの適用範囲、
  Acquire/Submit/Presentのバイナリセマフォ、転送メタデータ順序を追跡する。
- 無効時: nullable分岐とdeviceの生成状態フラグ以外の診断用追跡を作らない。
  診断用コールバック、状態再生、生成フィールド表、配置検証用リフレクションは
  実行しない。ホストメモリの範囲・オーバーフロー検査、必要なbinding情報、
  ネイティブエラー処理は残す。
- GPU内部の分岐・動的インデックス、直接のネイティブ操作、unsafeによる
  stagingバイト列の書き換えは観測できない。アクセスは登録範囲と宣言した
  ステージに基づいて保守的に扱う。実行の一部がネイティブへ発行された後の
  失敗は巻き戻せず、非同期失敗では追跡したキュー状態を不明として扱う。

公式Fallout runnerによる検証:

    dotnet run --project build -c Release -- --target Test --host-only --configuration Release
    dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Tests --configuration Debug
    dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Tests --configuration Release
    LUMYTE_GRAPHICS_GPU_TESTS=1 VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Tests --configuration Release
    dotnet run --project build -c Release -- --target CheckMarkdown

- 全ホストRelease: 2,972成功、失敗0、101スキップ。
- Graphics Debug/Release: 各179成功、失敗0、80スキップ。
- 実GPU Graphics Release: 255成功、既知のVulkan深度形式失敗2、
  native presentationの2テストをスキップ。有効化したVulkan/Wgpu転送は成功。
- Direct3D12の管理側Releaseコンパイルは成功。Windows実機は未検証。
- Markdown検査、差分の空白検査は成功。

native presentationは実行成功を主張しない。所有するWeston/Xwaylandでの
検証を試みたが、Lumyte起動前のcompositor終了時クラッシュと、独立した
Xwaylandのキーボード初期化失敗で有効な表示環境を確立できなかった。
対応するVulkan/Wgpuテストは追加済みだが、動作する表示環境なしでは
スキップする。Browser実行、Windows D3D12、NativeAOT、全ホストDebugも
今回の実行範囲には含まない。

実行ログはartifacts/validation2-{debug,fullhost,hardware,d3d,markdown}.logと
同じartifacts内のcompositor/Xwaylandログに保持する。
過去ADR0009/0015/0018/0022/0023はef61258fと同一のまま維持する。

## D3D12部分提出の所有権修正（2026-11-13）

wait・signalを個別に発行するD3D12経路では、最後のcompletion signalだけを
成功境界にすると、途中まで成功した操作を失う。各native wait／signalが
成功した直後に、その操作だけを同じcompletion ownerへ反映するよう修正した。
未発行・失敗した操作を成功として公開せず、commandsが空でも、発行済み操作が
あれば回復用ownerをdeviceへ保持する。producerの完了だけではconsumer waitの
使用中状態を退役させない。

部分発行後はqueue状態を不明として扱う。元の例外をそのまま返し、回復用markerの
発行失敗やpending fenceのDispose拒否で上書きしない。回復用markerはGPU待機を
強制せず、実際の完了観測まで依存を保持する。必要な場合の
WaitForRetirementAsyncは明示的な回復待機、またはnative device removalの
確認後にだけownerを退役させる。キャンセルや早期Disposeは完了の代わりにならない。
最初のnative操作より前の失敗では、未発行fence専用の解放経路を使う。

通常の無効経路に診断用collectionやcallbackは追加しない。部分発行時のnative
cleanup所有権は既存submissionと既に作成したdescriptor配列、deviceのlinked ownerで
維持し、失敗後のowner保存にも新しいlistやnodeを確保しない。native transportの
内部seamにより、公開deviceをforwarding wrapperにせず実際のqueue経路を検査する。

管理側のnative-call fault injectionによる24ケースを追加した。第2wait、第2signal、
最終completion signal、semaphore-only提出、最初のwait／signal／executeの失敗、
元の例外identity、未発行signalの非公開、producer完了後のconsumer保持、キャンセル、
実完了または確認済みdevice removalでの退役を有効／無効の両方で検査する。
既存のD3D12 parityテストに残っていたDebug限定期待値も、明示的な診断呼び出しが
両構成で同じ契約になるよう揃えた。

- 旧来の「最後に一括公開・commandsだけを見て保持」のqueue処理を同じnative seamで
  一時的に復元したnegative control（HRESULT境界2ケースの追加前）は、
  失敗19、成功7、Windows nativeスキップ7。
  修正済みsourceへ戻したことを確認してから最終検証を実行した。
- Direct3D12 Debug／Release: 各28成功、失敗0、Windows nativeスキップ7（35件）。
- Graphics Debug／Release: 各179成功、失敗0、80スキップ。
- 全ホストRelease: 2,972成功、失敗0、101スキップ。
- Windowsの実D3D12実行は引き続き未検証。fault injectionはnative GPU命令を
  発行しない管理側のqueue／semaphore／submission経路の検証である。

実行コマンドは同じ公式runnerのTest targetへ、
project=tests/Graphics/Lumyte.Graphics.Direct3D12.Testsと
configuration=Debug／Releaseを渡す。ログは
artifacts/validation3-{d3d-debug,d3d-release,graphics-debug,fullhost,negative-control}.logに
保持する。negative controlのsourceと復元用sourceもignored artifacts内だけに保存し、
実装・履歴には取り込まない。

## Registration-owned access follow-up (2026-11-13)

Starting at 4e58e198, raw-buffer WriteBuffer selects ShaderBufferAccess.ReadOnly
(default) or ReadWrite at registration. Usage flags remain allocation capabilities;
registration validates the selected capability. Reference-containing ShaderDataSlice
registrations remain read-only. IGpuRef stays opaque and unchanged, and GetElement
follows the original immutable registration. Replacement creates new metadata;
old references are never executed after replacement. ADR0014's caller-owned lifetime
contract is unchanged: no generations, invalidation tracking or lifetime rescans.

WriteReference now receives only the C# offset and reference. ShaderValue has no
runtime Path, the generator emits no reference-path argument, and Pack callbacks
no longer receive shader kind strings. Wgpu/Browser classify reachable references
from registration access, including transitive references. Separate read/write
registrations of the same range use separate pool/remap lanes, without accidentally
filling both lanes from the allocation's capabilities. D3D12 selects SRV/UAV by
registration; Vulkan physical addresses and D3D12 absolute-offset encoding remain
unchanged. Shader resource kind/type/access consistency is a Debug diagnostic,
not a Release admission rule. Host slot bounds, duplicate offsets, reference ABI
length, selected-table membership and registration capability checks remain active.

ArgumentTableExercise now checks read-only/writable capabilities, invalid enum values,
GetElement access, distinct registrations of one allocation and replacement metadata.
It runs through the existing Vulkan/Wgpu hardware argument-table tests and Windows
D3D12 parity exercise. A deterministic CPU theory checks read/write pool deduplication
and each remap lane for two aliased ranges, plus deliberately reversed shader access:
Debug diagnoses the mismatch; Release still uses registrations for classification.
No intentionally invalid shader root is submitted to the GPU.

Use the activation/TMPDIR setup below and the official Fallout runner:

    dotnet run --project build -c Release -- --target Test --host-only --configuration Release
    LUMYTE_GRAPHICS_GPU_TESTS=1 VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Tests --configuration Debug
    LUMYTE_GRAPHICS_GPU_TESTS=1 VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Tests --configuration Release
    dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.RenderGraph.Tests --configuration Debug
    dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.RenderGraph.Tests --configuration Release
    dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Direct3D12.Tests --configuration Debug
    dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Direct3D12.Tests --configuration Release
    dotnet run --project build -c Release -- --target CheckMarkdown

Final targeted results:

- Hardware Graphics Debug and Release: each 167 passed, 2 failed, 0 skipped
  (169 total). Only the known VulkanAdvancedCommandsAsync and
  VulkanUncachedAdvancedCommandsAsync unsupported depth-format paths fail.
  The registration capability/GetElement/replacement checks and new alias/remap
  theory pass; no native assertion or GPU-test eligibility was relaxed.
- Full host Release: 2,886 passed, 0 failed, 97 skipped (2,983 total,
  40 test projects). Official restore/compile/test all succeeded; compile reported
  0 warnings/errors. No full-host Debug run was performed.
- Official CheckMarkdown: 311 files, 0 issues; git diff --check passed.
- RenderGraph Debug and Release: each 124 passed, 0 failed/skipped.
- Direct3D12 Debug and Release Linux CPU tests: each 4 passed, 0 failed,
  7 Windows-native tests skipped. Managed backend source compiled in both modes.
- Drawing Debug and Release CPU tests: each 12 passed, 0 failed,
  10 native GPU tests skipped. Null references still fail host snapshot capture
  in both modes; shader-schema checks alone are Debug-only.

The initial host compile exposed remaining callback fixture signatures and test
analyzer diagnostics; those were corrected before the final full-host run. Generated
build-schema drift is restored before committing. No CI/configuration files changed.

Logs use the registration- prefix under artifacts/shader-transfer. Hardware was
reverified with the Radeon ICD: Radeon 8060S Graphics (RADV STRIX_HALO), Mesa
26.0.8-1ubuntu0.3; .NET SDK 10.0.403 and Slang 2026.19. Actual Browser/WASM WebGPU,
Windows native D3D12, NativeAOT and simultaneous read/write alias GPU execution are
not validated by the CPU remap test. Native alias-use constraints still apply.
No performance claim is made. Protected historical ADRs 0009/0015/0018/0022/0023
remain byte-identical to ef61258f; only the unmerged ADR0035 is revised.

## Ordinary unmanaged transfer follow-up (2026-11-13)

This follow-up starts at b27fce00 on the same isolated main-based branch. Ordinary
nonpartial structs now use the existing `IGraphicsBuffer<T>`/`BufferSlice<T>` path,
not an IShaderData wrapper, generated codec, registration, or per-element snapshot.
`CreateShaderDataBuffer<T>` selects this unmanaged overload and returns raw storage;
classes implementing IShaderData retain the metadata-bearing allocation path.
The overloads have different parameter lists, not constraints-only signatures.
Native and graph SetArguments accept ordinary unmanaged root structs as well.

DrawingVertex, ModelVertex, ModelMaterial, BindingVector, BindingPackedVector,
BindingNestedVector and BindingCameraArguments no longer need transfer interfaces
or partial declarations. ModelMesh exercises the ordinary factory in production.
The GPU binding regression loads two elements of the ordinary vector, packed and
nested sample structs, checking scalar/vector offsets and array stride. New public
API regressions compare explicit padding and raw bytes, immediate capture, plain
root overload resolution, Debug-only layout errors, graph-owned raw allocations,
and two-element external System.Numerics.Vector3 upload/copy/readback on both
native backends. Generated reference-containing class tests remain intact.

Environment: .NET SDK 10.0.403, Slang 2026.19, Linux x64, Radeon 8060S Graphics
(RADV STRIX_HALO), Mesa 26.0.8-1ubuntu0.3. Hardware was reverified using
VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json; the identity is retained
in artifacts/shader-transfer/unmanaged-hardware.txt. Activation and TMPDIR are
the same as the commands below. All commands use the official Fallout runner.

- Hardware Graphics Debug and Release: each 165 passed, 2 failed, 0 skipped
  (167 total). The only failures are the existing VulkanAdvancedCommandsAsync
  and VulkanUncachedAdvancedCommandsAsync unsupported depth-format paths.
  Both new external-vector GPU transfer tests and ordinary-struct shader loads pass.
- RenderGraph Debug and Release: each 124 passed, 0 failed/skipped.
- Drawing Debug and Release (CPU): each 12 passed, 0 failed, 10 GPU tests skipped.
- Direct3D12 Debug and Release (Linux CPU parity): each 4 passed, 0 failed,
  7 Windows-native tests skipped (11 total). Managed backend source compiled.
- Full host Release compile: 0 warnings/errors.
- Final full host Release suite: 2,884 passed, 0 failed, 97 skipped (2,981 total,
  40 test projects). The official runner restored, compiled and tested successfully.
- Official CheckMarkdown: 311 files linted, 0 issues. git diff whitespace validation
  passed; runner-generated build schema drift was restored before committing.

The initial host run caught two test-fixture assumptions: a cleanup proxy
intercepted only CreateBuffer rather than the new factory, and graph reflection
expected only one SetArguments overload. Those fixtures now preserve and check
both overload contracts; the cleanup failure and retry assertions are unchanged.
The old numeric-layout rejection test now asserts Debug rejection and exact raw
bytes in Release, matching the authorized diagnostic-only contract. Resource-reference
rejection tests remain unchanged. The new graph raw-byte accessor now reads its known C# offset rather than
requiring diagnostic Fields in Release. The initial failed log is retained separately.

Ordinary struct Debug diagnostics cache managed field metadata and actual managed
offsets when dynamic code is supported. Without it (Browser/Debug AOT), ordinary
values still transfer and Debug checks names/sizes without field-offset probes.
Release compiles out the probes and has no per-field reflection or runtime code generation. No conversion or shader
layout repair is performed. Generated class metadata remains AOT-safe. Actual
NativeAOT, Browser WebGPU and Windows D3D12 execution remain unverified; ModelMesh
3D GPU pixels were not run in this follow-up. No performance claim is made.

Reproduction uses the existing commands below, with these additional targeted runs:

    dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Direct3D12.Tests --configuration Debug
    dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Direct3D12.Tests --configuration Release

All follow-up logs use the unmanaged- prefix under artifacts/shader-transfer.
Protected historical ADRs 0009/0015/0018/0022/0023 remain byte-identical to ef61258f.
Only the current GRAPHICS-0035 ADR is revised; its fixed baseline metadata is retained.

## PR branch provenance

The isolated PR is based on origin/main `ef61258f27ec0f19240a5333d4388f1a3ac9de50`,
not the original development base. It cherry-picks only the shader migration, adapting
main's existing graph ownership and argument-partition contracts without importing
unmerged UI, benchmark or render-graph optimization commits.

Fresh PR-branch verification is recorded below separately. The historical full-suite
results in this document are **not evidence for the new main-based branch**.

## Fresh main-based PR verification

On 2026-11-13 the isolated checkout used .NET SDK **10.0.403** (selected by
its current mise definition), Slang 2026.19 and the Radeon/Mesa adapter above.
The worktree restored its own NuGet/build/native artifacts rather than reusing
another checkout's bin/obj. Hardware identity is retained in
artifacts/shader-transfer/hardware.txt. Commands use the activation and TMPDIR
shown above and the official Fallout runner.

    dotnet run --project build -c Release -- --target Compile --host-only --configuration Release
    dotnet run --project build -c Release -- --target Test --host-only --configuration Release

Hardware-enabled Graphics uses VK_DRIVER_FILES and LUMYTE_GRAPHICS_GPU_TESTS=1
with the two Graphics commands shown above. CPU graph/drawing checks use:

    dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.RenderGraph.Tests --configuration Debug
    dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.RenderGraph.Tests --configuration Release
    dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Drawing2D.Tests --configuration Debug
    dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Drawing2D.Tests --configuration Release
    dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Drawing.Tests --configuration Debug
    dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Drawing.Tests --configuration Release

- Host Release compile: succeeded, 0 warnings/errors.
- Drawing2D Debug/Release: each 309 passed, 0 failed/skipped.
- Drawing Debug/Release CPU suite: each 12 passed, 0 failed, 10 GPU tests skipped.
- RenderGraph final Debug and Release: each 122 passed, 0 failed/skipped.
  The initial Debug run caught
  a migrated test accessor reading numeric bytes at zero instead of its explicit
  C# field offset; that accessor was corrected before the final rerun.
- Final hardware Graphics Debug/Release: each **159 passed, 2 failed, 0 skipped**
  (161 total), including all new bulk-wire and main-specific native graph tests.
  Only VulkanAdvancedCommandsAsync and VulkanUncachedAdvancedCommandsAsync fail
  at unsupported depth texture creation. No new bulk-transfer test fails.
- Full host Release: **2,878 passed, 0 failed, 95 skipped** (2,973 total,
  40 test projects); official runner restore, compile and test all succeeded.
- Official CheckMarkdown: 310 files linted, 0 issues. Touched ADRs were migrated
  to the current metadata/section rules; the frozen legacy exemption manifest was
  not modified. git diff --check passed; runner-generated build schema drift was restored.
- Full host Debug was not rerun on this new base; only the targeted Debug projects
  listed above were rerun.

The native failures are the same two Vulkan advanced depth-format paths seen in
the historical baseline. The texture creation and advanced-command sources are
unchanged from ef61258f; no assertions or GPU-test eligibility were relaxed.
This is not a fresh pristine-ef61258f hardware baseline measurement.

## Historical development-branch environment

Validation on 2026-11-13 used .NET SDK 10.0.401, Slang 2026.19, Linux x64,
and Radeon 8060S Graphics (RADV STRIX_HALO), Mesa 26.0.8-1ubuntu0.3.
Hardware identity was verified with vulkaninfo under the selected Radeon ICD.
No timing/performance claim is made.

All builds and tests use the official Fallout runner from build/README.md.
The local environment activation used:

    export LUMYTE_ENV_ROOT=/home/hideaki/.openclaw/workspace/Lumyte/artifacts/dev-env
    source tools/setup/activate.sh
    export TMPDIR=/home/hideaki/.openclaw/workspace/shader-transfer-tmp
    mkdir -p "$TMPDIR" artifacts/nuget

The TMPDIR override avoids an unrelated /tmp per-user quota exhaustion. No machine
configuration, CI definitions, driver settings or scheduler files were changed.

## Historical development-branch commands

    dotnet run --project build -c Release -- --target Test --host-only --configuration Debug
    dotnet run --project build -c Release -- --target Test --host-only --configuration Release
    dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Direct3D12.Tests --configuration Debug
    dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Direct3D12.Tests --configuration Release

Hardware-enabled native tests use the same runner:

    export VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json
    export LUMYTE_GRAPHICS_GPU_TESTS=1
    dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Tests --configuration Debug
    dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Tests --configuration Release

Unset LUMYTE_GRAPHICS_GPU_TESTS for the ordinary CPU-only host suite. Physical uniform
padding, scalar GPU loads, original reference encodings, nested/vector/matrix data,
cyclic references, explicit copies and mutable argument snapshots are exercised by
the native Vulkan/Wgpu shader binding, drawing and effect tests.

## Historical development-branch results

- Full Linux host Debug: compile succeeded; 2,521 passed, 2 baseline failures,
  67 skipped (2,590 total, 40 test projects).
- Full Linux host Release: compile succeeded; 2,523 passed, 0 failed,
  67 skipped (2,590 total, 40 test projects).
- Hardware-enabled Graphics Debug and Release, each: 127 passed, 2 baseline
  depth-format failures, 0 skipped (129 total). All 46 other GPU tests pass.
- Direct3D12 Debug and Release CPU parity, each: 4 passed, 0 failed,
  5 Windows-native tests skipped (9 total).
- Pristine baseline Debug: Resources 226 passed/1 failed; Animation 144 passed/1 failed.
  Hardware Graphics 105 passed/2 failed (107 total), reproducing exactly the same
  unsupported Vulkan depth-format failures.
- Final git diff whitespace validation passed. All intended code was compiled in both
  configurations; generated build schema drift from the official runner was restored.

## Historical baseline isolation

The pristine baseline is commit 7ed00bbd1bbe47c7e2c98304bfda8a452a7d2630.
An unmodified git archive of that commit was used to reproduce unrelated failures with
the same SDK, driver and official runner:

    dotnet run --project build -c Release -- --target Test --project tests/Core/Lumyte.Resources.Tests --configuration Debug
    dotnet run --project build -c Release -- --target Test --project tests/Animation/Lumyte.Animation.Tests --configuration Debug
    dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Tests --configuration Debug

The first two reproduce the existing Debug allocation-budget failures in
WarmOwnershipOperationsHaveBoundedAllocationsAsync (reference) and
WarmedNumericUpdatesAndRepeatedTransitionsAllocateNoManagedMemory. The GPU baseline
reproduces both Vulkan advanced-command depth-format failures on this adapter.
Assertions were not weakened, skipped or rewritten to hide these baseline failures.

## Coverage boundaries

- Native Vulkan and Wgpu execute on the verified Radeon adapter; the two unsupported
  Vulkan advanced depth-format paths remain baseline failures.
- Browser projects compile in the full host solution, but actual Browser/WASM/WebGPU
  execution is not validated by native tests.
- Direct3D12 CPU schema tests can run on Linux. Windows native D3D12/Agility execution
  remains unverified and must not be inferred from DXIL compilation.
- ModelVertex/ModelMaterial/ModelDrawArguments schemas are checked against all three
  compiled shader targets, but a ModelMesh 3D GPU pixel-rendering smoke was not run.
- NativeAOT publication and browser publication are not part of this validation.

Logs and TRX files are retained under ignored artifacts/shader-transfer and
artifacts/test-results. Generated WGSL, SPIR-V, DXIL and reflection are not committed.
