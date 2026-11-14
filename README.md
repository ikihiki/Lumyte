# Lumyte

C# を中心に、DirectX／Vulkan の Native バックエンドに C++ を使用するゲームエンジン。
文字組みにはRustのParleyを使い、COLR v1とMSDF/MTSDFを共通の2D描画へ接続します。

ビルド・テスト・publish・packは、公式構成のFallout runnerから実行します。

```sh
dotnet run --project build -c Release -- --target Compile
dotnet run --project build -c Release -- --target Test
```

必要なcompiler・SDKの準備は開発環境のセットアップを参照してください。

- [サンプル一覧と共通ランチャー](samples/README.md)
- [テストの分類と実行手順](tests/README.md)／[ベンチマーク一覧](benchmarks/README.md)
- [開発環境のセットアップ](docs/development-environment.md)
- [共通ビルド手順](build/README.md)／[ビルド工程の設計](docs/adr/0004-build-orchestration.md)
- [ADR の書き方と運用](docs/adr/0001-adr-writing-policy.md)
- [リポジトリのフォルダ構成](docs/adr/0002-repository-layout.md)
- [GraphicsのDI・デバイスロスト復旧](docs/adr/graphics/GRAPHICS-0016-device-di-and-loss-recovery.md)／[Hosting API](src/Graphics/Lumyte.Graphics.Hosting/README.md)
- [mise による共通開発環境の設計](docs/adr/0003-development-environment.md)
- [ワールドとスタック](src/Core/Lumyte.Worlds/README.md)／[設計](docs/adr/core/CORE-0002-world-stack.md)
- [汎用数学計算](src/Core/Lumyte.Mathematics/README.md)
- [設定管理](src/Core/Lumyte.Settings/README.md)／[設定管理の設計](docs/adr/settings/SETTINGS-0001-user-settings-persistence.md)
- [DI で通信方式を選択するゲームエンジン診断システム](docs/adr/diagnostics/DIAGNOSTICS-0001-diagnostics-transport.md)
- [設定の診断公開](src/Diagnostics/Lumyte.Diagnostics.Settings/README.md)／[設定診断の設計](docs/adr/settings/SETTINGS-0002-diagnostic-operations.md)
- [アニメーションの診断公開](src/Diagnostics/Lumyte.Diagnostics.Animation/README.md)／[アニメーション診断の設計](docs/adr/diagnostics/DIAGNOSTICS-0003-animation-operations.md)
- [Windowの診断公開](src/Diagnostics/Lumyte.Diagnostics.Windowing/README.md)／[Window診断の設計](docs/adr/diagnostics/DIAGNOSTICS-0005-window-inspection.md)
- [診断基盤の API と利用方法](src/Diagnostics/Lumyte.Diagnostics/README.md)
- [診断 Input サンプル](samples/Lumyte.Diagnostics.Sample/README.md)
- [OpenAL オーディオ](src/Audio/Lumyte.Audio.OpenAL/README.md)／[Web Audio](src/Audio/Lumyte.Audio.Browser/README.md)／[共通 API](src/Audio/Lumyte.Audio.Abstractions/README.md)
- [Audioの型付き配送](src/Audio/Lumyte.Audio.Execution/README.md)／[OpenAL実行例](samples/Lumyte.Audio.Execution.Sample/README.md)／[WebAudio実行例](samples/Lumyte.Audio.Execution.Browser.Sample/README.md)
- [Audioリソースの実装](src/Audio/Lumyte.Resources.Audio/README.md)／[AudioのDI登録](src/Audio/Lumyte.Resources.Audio.DependencyInjection/README.md)
- [オーディオ機構の設計](docs/adr/audio/AUDIO-0001-audio-system.md)
- [Input システムの設計](docs/adr/input/INPUT-0001-input-system.md)
- [Input の取得契約の分離](docs/adr/input/INPUT-0005-input-abstractions.md)
- [Platform・IWindow の設計](docs/adr/platform/PLATFORM-0001-platform-and-window-contracts.md)／[Window契約の分離と任意の Windowing 管理](docs/adr/window/WINDOW-0001-window-system.md)
- [Windowing の利用方法](docs/adr/window/WINDOW-0001-window-system.md#利用例)／[Windows の単一 Window サンプル](samples/Lumyte.Platform.Windows.Sample/README.md)
- [Clipboard の設計](docs/adr/platform/PLATFORM-0002-clipboard.md)
- [Input 診断](src/Input/Lumyte.Input.Diagnostics/README.md)／[監視・Tape・マッピング編集の設計](docs/adr/input/INPUT-0004-diagnostics-and-overrides.md)
- [コードスタイルと lint](docs/development-environment.md#コードスタイル)

- [文字描画・MSDF/MTSDF](src/Graphics/Lumyte.Graphics.Text/README.md)／[Parleyによる文字組み](src/Graphics/Lumyte.Graphics.Text.Parley/README.md)／[文字描画の設計](docs/adr/graphics/GRAPHICS-0024-text-rendering.md)
- [2D・3D共通画像エフェクトの設計](docs/adr/graphics/GRAPHICS-0017-image-effects.md)／[利用方法](src/Graphics/Lumyte.Graphics.ImageEffects/README.md)
- 2D描画の設計
  - [基本図形・paint・描画資源](docs/adr/graphics/GRAPHICS-0018-2d-graphics-engine.md)
  - [線・パス](docs/adr/graphics/GRAPHICS-0019-2d-paths-and-strokes.md)
  - [画像・mesh](docs/adr/graphics/GRAPHICS-0020-2d-images-and-meshes.md)
  - [文字向け形式・COLRv1](docs/adr/graphics/GRAPHICS-0021-2d-glyph-representations.md)
  - [RenderGraphレイヤー・Effects接続](docs/adr/graphics/GRAPHICS-0022-2d-render-graph-layers.md)
- [Depth／Stencil attachment](docs/adr/graphics/GRAPHICS-0010-depth-stencil-attachments.md)
- [Indexed drawとIndirect実行](docs/adr/graphics/GRAPHICS-0011-indexed-and-indirect-commands.md)
- [カラーテクスチャ形式の拡充](docs/adr/graphics/GRAPHICS-0012-color-texture-formats.md)
- [Surface・Swapchain・Present](docs/adr/graphics/GRAPHICS-0013-surface-swapchain-presentation.md)
- [利用者が管理するリソース寿命と実行時検証](docs/adr/graphics/GRAPHICS-0014-caller-managed-resource-validation.md)
- [PSOのシェーダープログラムと描画状態の分離](docs/adr/graphics/GRAPHICS-0008-pipeline-programs-and-render-state.md)
- [Argument Tableのシェーダー引数への接続](docs/adr/graphics/GRAPHICS-0009-shader-argument-binding.md)
- [C#の配置を正とするシェーダー一括転送](docs/adr/graphics/GRAPHICS-0035-csharp-authoritative-shader-transfer.md)
- [CommandBufferと明示的なGPU実行](docs/adr/graphics/GRAPHICS-0007-command-buffers-and-submission.md)
- [シェーダーの設計](docs/adr/graphics/GRAPHICS-0006-shader-compilation-and-modules.md)／[Slang コンパイラー](src/Graphics/Lumyte.Graphics.Shaders/README.md)
- [Argument Tableと型付きGPU参照](docs/adr/graphics/GRAPHICS-0005-argument-tables-and-gpu-references.md)
- [サンプラーの設計](docs/adr/graphics/GRAPHICS-0004-samplers.md)
- [テクスチャとViewの設計](docs/adr/graphics/GRAPHICS-0003-textures-and-views.md)
- [型付きbufferの設計](docs/adr/graphics/GRAPHICS-0002-typed-buffers.md)
- [グラフィックデバイスの設計](docs/adr/graphics/GRAPHICS-0001-graphics-device.md)
- [Graphics.Abstractions](src/Graphics/Lumyte.Graphics.Abstractions/README.md)
- [RenderGraph の利用方法](src/Graphics/Lumyte.Graphics.RenderGraph/README.md)／[設計](docs/adr/graphics/GRAPHICS-0015-render-graph.md)／[診断連携](src/Diagnostics/Lumyte.Diagnostics.RenderGraph/README.md)
- [2D描画の利用方法](src/Graphics/Lumyte.Graphics.Drawing2D/README.md)／[Resourcesからの生成](src/Graphics/Lumyte.Resources.Graphics.Drawing2D/README.md)／[RenderGraph連携](src/Graphics/Lumyte.Resources.Graphics.Drawing2D.RenderGraph/README.md)
- [Wgpu](src/Graphics/Lumyte.Graphics.Wgpu/README.md)／[Browser](src/Graphics/Lumyte.Graphics.Browser/README.md)／[Vulkan](src/Graphics/Lumyte.Graphics.Vulkan/README.md)
- [DeviceCaps サンプル](samples/Lumyte.Graphics.DeviceCaps.Sample/README.md)／[Browser サンプル](samples/Lumyte.Graphics.Browser.Sample/README.md)
- [Direct3D 12 バックエンド](src/Graphics/Lumyte.Graphics.Direct3D12/README.md)／[Direct3D 12 サンプル](samples/Lumyte.Graphics.Direct3D12.Sample/README.md)
- [Composition の設計](docs/adr/composition/COMPOSITION-0001-declarative-composition.md)
- [Composition の利用例](samples/Lumyte.Composition.Sample/README.md)
- [UIツリー・レイアウト・描画・ポインター入力の利用方法](src/UI/Lumyte.UI/README.md)／[直接描画サンプル](samples/Lumyte.UI.Sample/README.md)／[描画の設計](docs/adr/ui/UI-0004-drawing-and-clipping.md)／[入力の設計](docs/adr/ui/UI-0005-pointer-dispatch-and-capture.md)
- [アニメーションシステムの設計](docs/adr/animation/ANIMATION-0001-animation-system.md)
- [実行基盤のAPIと使い方](src/Core/Lumyte.Execution/README.md)／[Source generator](src/Core/Lumyte.Execution.Generators/README.md)／[実行基盤・DIサンプル](samples/Lumyte.Execution.Sample/README.md)／[実行基盤の設計](docs/adr/core/CORE-0003-execution-and-jobs.md)
- [汎用状態機械の設計](docs/adr/core/CORE-0001-state-machine.md)／[利用例](samples/Lumyte.StateMachines.Sample/README.md)
- [アニメーション状態機械の構築とイベント時刻](docs/adr/animation/ANIMATION-0003-animation-execution-and-event-time.md)／[利用例](samples/Lumyte.Animation.StateMachine.Sample/README.md)
- [形式に依存しない補間・値ソースの合成](docs/adr/animation/ANIMATION-0004-format-independent-value-sources.md)
- [アニメーションの利用例](samples/Lumyte.Animation.Sample/README.md)

ソリューションは `Lumyte.slnx` を使用します。Falloutの`Test` targetで、
`tests/` 内のソリューションに登録されたテストを実行します。
CI は Linux／Windows の x64／aarch64 で、mise セットアップ後に同じFalloutの検証・ビルド・テスト工程を実行します。
すべての push と PR を対象に、TRX 形式の結果を構成ごとに保存します。

- [デバイス補正・仮想デバイスの設計](docs/adr/input/INPUT-0002-processing-and-recognition.md)
- [アクション加工・認識・入力管理の設計](docs/adr/input/INPUT-0003-actions-and-contexts.md)

診断の実通信: [サーバー起動](src/Diagnostics/Lumyte.Diagnostics.Server/README.md)、[ゲーム側サンプル](samples/Lumyte.Diagnostics.Remote.Sample/README.md)、[検証結果](docs/diagnostics/communication-verification.md)。

診断サーバーの[同梱Web UI](src/Diagnostics/Lumyte.Diagnostics.Server/README.md#web-ui)はサーバーのURLから利用できる。[配信・認証・更新の設計](docs/adr/diagnostics/DIAGNOSTICS-0002-server-hosted-ui.md)を参照する。

診断画面の見た目は [GitHub Pagesの画面プレビュー](https://ikihiki.github.io/Lumyte/) でmain・PRごとに確認できます。

## struct限定の転送

Every native/root, shader-data buffer, slice, factory, Graph and resource declaration
consuming IShaderArguments requires `where T : struct, IShaderArguments`; managed
references are allowed, so this is not an unmanaged constraint. Generated providers
are mutable partial structs/record structs with private unmanaged wire and typed
IGpuRef members. Classes, record classes and ref structs are rejected with LUMG001
even with handwritten codecs. A manual class can implement the C# interface, but
consumer generics reject it with CS0453 even without the generator.
Generated readonly Write copies only the wire before getters; generic in calls
and non-readonly getters may defensively copy the outer struct. No zero-outer-copy
or allocation-free guarantee follows. Generated partial properties preserve the exact
readonly modifiers of their declarations; a plain get does not gain readonly.
Argument Capture retains DiagnosticType and a cached static generic DiagnosticFields
callback, never the input struct, boxing or an instance closure. Capture and Copy
do not invoke it; the first explicit GetDiagnosticFields accesses the lazy
`ArgumentDiagnosticCache<T>` through direct T.TransferFields static dispatch.
`Fields<T>(in T)` uses the same AOT-safe path. Type-only unknown-T inspection and raw
unmanaged reflected offsets require dynamic code and are unavailable on AOT.
Handwritten unmanaged arguments with default empty TransferFields keep the JIT
reflected-field fallback; managed outer structs without static metadata return
empty diagnostics, never numeric transfer of their managed layout.

Callers choose whole-element upload ranges, with no dirty tracking, field ranges,
automatic coalescing or hidden value cache. Edit array elements by ref or write back
a local copy; then explicitly CopyFrom and CopyBuffer the chosen range. Keep upload
sources unchanged and alive until consumed by GPU work. default(T)/array elements
skip constructors and leave reference members null until filled by the caller;
null IGpuRef in a default root is rejected during encoding. DrawingInstruction
new() preserves resource indices of -1 and initializes references with default!;
this nullable-analysis suppression does not create valid GPU references.
Browser build success is not AOT publication or runtime AOT execution evidence;
AOT publication and execution have not been tested.

## シェーダーデータ転送

Ordinary unmanaged C# structs need no IShaderArguments, partial declaration,
generator or registration. `CreateShaderDataBuffer<T>` returns raw `IGraphicsBuffer<T>`
storage; CopyFrom preserves C# bytes, padding and stride. Use BufferBarrierDesc,
CopyBuffer and argument-table WriteBuffer explicitly. SetArguments(in value) also
accepts unmanaged root structs. Reference-containing values use mutable partial structs/record structs implementing
IShaderArguments with private unmanaged wire storage. Shader layout must
match C#; diagnostics require explicit per-device EnableValidation() in both Debug and Release. See the [transfer contract](docs/adr/graphics/GRAPHICS-0035-csharp-authoritative-shader-transfer.md).

Native Vulkan roots now use vkCmdPushConstants; Direct3D12 uses graphics/compute
root 32-bit constants, not per-Set GPU uniform/CBV allocation or an oversized-root
fallback. Query VulkanDevice.MaxPushConstantsSize; D3D12 exposes
RootConstantDwordCapacity=58 and RootConstantByteCapacity=232. Set writes a cleared, bounded ceil(wireSize/4)-word stack span and records
native constants before returning. Complete, optional diagnostics, reference preparation,
metadata creation all precede emission. The driver command buffer
owns the numeric copy, not the temporary stack. Draw/Dispatch never re-emit it;
selected roots retain immutable metadata, with references only when collected, not numeric
CPU heap arrays or ArrayPool rentals. Metadata and execute-time bindings still allocate.
記録済みbindingは機能上必要な登録とtable identityを保持し、検証ONの場合だけ追加の依存revisionを保持します。 No root
metadata KeepBinding or IDisposable is required at Set. Wgpu/Browser keep their
existing private concrete IDisposable RootStorage, which owns its uniform and
once-initialized Arguments; bindings Seal it directly and commands KeepBinding
the owner until GPU release.

Artifact-capacity preflight is mandatory ABI safety, independent of EnableValidation:
Vulkan checks against the owning device before shader-module creation; all D3D12
pipeline/partial paths check before native pipeline creation. Both inspect every app
ConstantBuffer by its actual root name using logical ShaderDataLayout.Root.Size,
excluding lumyteDrawInfo and __lumyte_schema_ entries. A logical 232-byte D3D12 root
with a padded 240-byte declaration is valid: it loads 58 DWORDs, not 60.
Vulkan’s observed 256-byte limit is device-specific, not a universal capacity.
Wgpu/Browser retain backend-private uniform-buffer emulation without new immediate
APIs or dependency upgrades. Shared/Graph lifetimes and metadata remain transport-neutral;
target-specific shader emission is inherently target-aware, not runtime feature dispatch.
The archived writer measurements describe checkpoint 3582af8a, not this newer
constant-root implementation; see the [pinned report](docs/performance/backend-native-writers.md).

Vulkan fixes push ranges per bind point: offset zero and physical capacity, with
Compute for every compute variant and Vertex|Fragment for every graphics/partial
variant. Each Set emits exactly its range’s stage mask, preserving separate native
compute/graphics push state. Descriptor visibility may independently include all
three stages; descriptor-count layouts may vary, not the bind-point root contract.
A backend host guard rejects roots above 64 KiB
without fallback even on pathological physical limits (256 bytes is device-specific).
D3D12 initializes one thread-safe cached fixed root signature per device before Set:
58 app + 1 + 1 tables + 2 draw + 2 SRV = 64 DWORDs. Separate graphics/compute
root pointer tracking conditionally binds the same signature for PSO/SetProgram
changes without resetting constants. Validation/preparation failure emits no
native constants and preserves the old selection; issued void driver commands
and device failures are not rollbackable. Ref-struct writers require C# 13+ and
the pinned .NET 10 SDK; `Write<TWriter>` adds only allows ref struct to its constraint.

Vulkan/D3D12 render and compute encoders guard codec/preparation with
_settingArguments: reentrant Set, pipeline changes, End and Draw/Dispatch are
rejected. SetArgumentTable remains allowed for Graph preparation’s autobind.
After callbacks, the captured pipeline’s ValidateAlive, owner.ValidatePass and
pipeline-identity check all precede native emission, rejecting
raw command-buffer or pipeline disposal before native use. Host failure preserves
the old root selection, but cannot undo caller-requested resource disposal, table
autobinding, or already-issued driver commands.

Vulkan correctness review includes source-level range/stage-mask checks against
VUID-vkCmdPushConstants-offset-01796 and actual hardware tests. The optional
VK_LAYER_KHRONOS_validation layer is absent here; hardware success is not a native
validation-layer result. RootRecordNative belongs to Set, not mutable PSO state;
Const_Metadata stays reference-only, with No_Frame_Source retention.

## 選択table全体のbindingと任意の依存検証

bindingは選択したArgumentTableの書き込み済み登録すべてから構築します。capacityの未登録holeはentryではありません。Vulkanは全texture／sampler登録をdescriptorへ投影し、buffer参照は保存済みGPU addressへoffsetを加えてinline符号化します。Direct3D12は全texture／sampler／ReadOnly SRV／ReadWrite UAV登録を投影し、各上限128／16／64／64を適用します。Wgpu／Browserは全登録から有限のbinding poolとremapを構築し、WGSLの個数はcapacityではなく実際の書き込み済み登録を基にします。native binding数・storage範囲・remap容量へ収まらなければ明確に拒否し、巨大な無効binding、追加feature・package・pool・cacheで回避しません。rootなしのshaderも明示選択tableの全登録をbindし、Wgpuは未使用のroot uniform binding 8を省略します。

全登録をbinding可能にすることは、全登録をGPU使用済みとして記録することではありません。backend-privateな登録・remapは検証OFFでも必要な機能状態ですが、同期の所有権や使用hazardを追加しません。resource寿命とCPU／GPU同期は利用者が管理します。

Graphのbarrier・依存順・cullingは、必要なresourceの正確で完全なpass宣言だけを使い、従来どおりallocation全体を保守的に同期します。検証OFFでは宣言の真実性・完全性が利用者の事前条件です。root走査、依存query、準備hook、許可集合、admission callbackの確保を行いません。table共有だけでは使用宣言になりません。

検証ONの場合だけ、Graphはrootとbuffer全体の推移的依存を、buffer allocationまたはtextureの元imageを正規化したresource object identityと宣言read／write権限の合併で照合します。native handle値の衝突判定、要素・byte範囲・view・callback取得元による許可判定は行いません。同じbufferの別宣言範囲から保存した参照も許可されますが、未宣言の別resourceとread-only宣言に対するReadWrite参照は拒否します。

GetGpuRefの具体化はこの粗い許可判定とは別です。要求offset／countと一致する登録を選び、単一要素なら既存登録からGetElementで導出できます。未登録の複数要素範囲に、より広いCountの参照を返しません。新しいslice APIや自動dirty trackingは追加しません。

## buffer全体の読み取り専用依存query

共通境界はIShaderDataSource.TryGetDependenciesだけです。ReadReferences(index)、SetTransferredReferences、SharedのShaderDataTransferStateおよびShaderBindingDependencies.Elementsは削除し、共通の書き込みAPIは設けません。

```csharp
using System.Diagnostics.CodeAnalysis;
using Lumyte.Graphics.Abstractions;

// IShaderDataSourceの宣言抜粋。手書きbackendも実装する。
public interface IShaderDataSource
{
    bool TryGetDependencies([NotNullWhen(true)] out IShaderReferenceMetadata? dependencies);
}
```

既存の有効なsourceに対する呼び出し例です。

```csharp
// falseを既知の空集合として扱わない。
if (source.TryGetDependencies(out IShaderReferenceMetadata? dependencies))
{
    foreach (IShaderReference reference in dependencies.References)
    {
        // 追跡済みのbuffer全体の依存を読み取る。
    }
}
else
{
    // 追跡利用不可。呼び出し側が宣言と同期を保証する。
}
```

検証OFFでは全backendが数値だけのReferenceCount = 0を含めてfalse／nullを返します。これは追跡利用不可であり、既知の空集合ではありません。登録・remapなどの非公開機能状態は残ります。検証ONではbuffer全体の不変な依存unionを返し、ReferenceCount = 0は既知の空集合としてtrueです。参照付きbufferの未初期化・失敗後の状態は無効で、queryはInvalidOperationExceptionとなり、buffer全体の明示再転送で回復します。部分転送だけでは回復しません。

既知状態への部分更新は旧依存とのunionを保持でき、buffer寿命内で保守的な偽陽性が残ります。全体書き直しは現在のunionを置き換えられますが、記録済みcommandが保持する旧revisionは不変です。要素ごとの配列や毎drawの行走査は不要で、検証ONのShaderBindingDependencies.Buffersが各bufferを一回だけ訪問します。

CPU CopyFromの更新はbackendが所有します。GPU copyは検証ONだけでbackend-privateなbuffer単位のcommand overlayを作り、native copy記録成功後に更新し、queue Submit成功後にだけ公開します。失敗・未送信・破棄したcommandから公開せず、隠れたreadback・待機・数値履歴も持ちません。

### 厳密な参照数とmapped batchの契約

IShaderArgumentsのstatic abstract ReferenceCountは重複も数えた物理16-byte slot数です。backend writerは全modeで0 <= N <= wireSize / 16、WriteReferenceの超過とCompleteの不足、位置・容量・型・範囲を検査します。rootの直接参照配列を収集するのは検証ONまたは利用者が明示したprepare hookがある場合だけです。それ以外はscalar countと単一の暗黙table hintだけを保持します。数値だけの追跡rootは共有empty metadataであり、未収集のnullとは区別します。

ShaderRootArgumentsはstorageを持たない不変metadata classです。Referencesはnullableでnullは未収集を表し、Createにprepareを渡す場合は利用可能なmetadataが必須です。rootの単一table hintは自動選択を補助しますが、明示SelectedTableが優先します。異なるtableが混在すればnative Set発行前に拒否します。

native CopyFromはbatch全体のmapped spanを一度だけ範囲・mapping方向・overflow検査し、row sliceへ直接書きます。Browserは一つの検査済みUint8Array viewへJS.setでコピーし、finallyでviewだけを解放します。検証ONだけHostWriteLeaseで再入書き込み・Map／Unmap／Disposeを診断します。失敗時は部分byte更新を許し、追跡buffer全体を無効にして元例外を保持します。OFFでは利用者がmappingと寿命・並行操作を保証し、追加lock・待機・pinは設けず、無効pointerを実行する試験もしません。

VulkanとD3D12はbuffer作成時の保存済みaddressへchecked offsetを加え、参照ごとのdriver queryやglobal cacheを作りません。native root即時定数、固定signature／stage、pipeline・End・callback後の再検査は継続します。残るbinding確保やBrowserコピーを含めzero allocation／zero copyは保証しません。c1bdf69以前の固定checkpoint測定はこの全table・任意追跡方式の性能結果ではありません。
