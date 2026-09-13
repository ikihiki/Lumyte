# 共通機能 RenderGraph

描画 library はこの assembly と `Lumyte.Graphics.Passes` のみを参照し、同じコンパイル済みコードを Native／Portable provider に提出する。共通 AddPass は機能の要求と論理的な読取り・書込みを宣言する。GPU command、shader、pipeline、descriptor／binding は各系統の pass 本体が扱う。

```csharp
var graph = new GpuRenderGraph();
var description = new GpuGraphTextureDescription(256, 256, GpuFormat.Rgba8Unorm);
var source = graph.CreateTexture("source", description);
var target = graph.CreateTexture("target", description);
var color = graph.CreateInput("clear", ClearValueInputContract.Instance,
    TextureClearValue.Color(new Vector4(0.25f, 0.5f, 0.75f, 1)));
graph.AddClearPass("clear", new(source, color));
graph.AddOutputPass("output", new(source, target, OutputEncoding.Srgb));
graph.ExportTexture(target);
var plan = graph.Compile();

var values = plan.CreateBindings();
values.Set(color, TextureClearValue.Color(new Vector4(1, 0, 0, 1)));
using var execution = await runtime.SubmitAsync(plan, values.Build(), cancellationToken);
await execution.WaitForCompletionAsync(cancellationToken);
var image = execution.GetExportedTexture(target);
// image の GPU 所有は execution にある。さらに長く使う場合は Resources.Pin(image) する。
```

`runtime` は [Hosting](../Lumyte.Graphics.Hosting/README.md) の session から借用する。手動 composition では `GpuRenderProviderRegistry` に実装を登録し、`GpuRenderRuntimeOptions.ProviderId` で選択する。`Auto` は最初の登録を選び、選択した provider の初期化失敗を別 provider への暗黙切替えで隠さない。

## API と所有

| API | 担当 |
| --- | --- |
| `IGpuRenderPassContract<TRequest,TResult>`、`GpuPassDeclarationContext` | 要求の Snapshot、外部 resource 使用、CPU input と upload 所有の宣言 |
| `GpuRenderGraph`、texture／buffer／dependency | CPU 上の graph。作成、借用 import、resource input、出力指定と Compile |
| `GpuGraphValue<T>`、`GpuGraphInput<T>`、`IGpuGraphInputContract<T>` | 定数／型付き slot、値の Snapshot と明示的な所有集合 |
| `GpuRenderGraphBindingsBuilder.Set/Build`、`GpuRenderGraphBindings.ToBuilder` | 変更から独立した不変値。各 Build は plan 内で一意な世代を取得する |
| `GpuRenderGraphPlan.Passes/Resources/Exports/Outputs` | 生存する機能と論理宣言。provider の内部 GPU pass 数とは異なる |
| `ValidateBindings`、pass の `RequestType/ResultType/GetInput` | 別 assembly の provider が使う公開拡張契約。要求型・入力・宣言を GPU 準備前に対応付ける |
| `GpuGraphResourceRef`、`GpuGraphTextureRef/BufferRef/PackageRef` | runtime と resource 世代を識別する非所有 ref。GPU handle は公開しない |
| `IGpuGraphResources.CreateScope/Pin/Collect/Trim` | 専用 ResourceManager の所有と回収への接続。`AcquireUse` は provider／presentation 向けの同期使用保持 |
| `scope.ImportPackageAsync/Release/Dispose` | 準備済み画像の GPU import と保持返却。ファイルロードはしない |
| `GpuBufferUploadData`、`GpuImageUploadData`、`GpuPackageUploadData` | 所有済み CPU bytes と export の集合。コンストラクタで借用 bytes／集合を固定する |
| `IGpuRenderRuntime.SubmitAsync/WaitIdleAsync/StopAccepting/DisposeAsync` | 使用保持、提出、drain、新規受理停止と所有者の終了 |
| `GpuGraphCompletion`、`GpuRenderGraphExecution` | GPU 使用終了と診断成功を区別する。export は Wait の成功後に取得する |
| `GpuRenderContext`、`GpuFrame`、`IGpuGraphPresentation` | 借用 runtime を用いた acquire／submit／present。未提出は Discard、受理不明は Retire |

Compile は shader／backend／DI を呼ばない。Read は先行内容、Write は全体初期化、ReadWrite は先行内容を保持する更新として扱う。不要な内容の writer を除去し、生存する reader と後続 writer の順序を維持する。古い内容を output に指定した後、それを上書きする pass も生存する場合は拒否する。古い内容を残すには別の resource に Copy してから export する。

plan／bindings は GPU 資源を所有しない。呼出し元は SubmitAsync まで scope／pin を維持し、provider は最初の await より前に使用保持を取得する。execution の Dispose は結果の所有を返すが、ResourceManager の batch は GPU 使用終了まで資源を保持する。待機取消しで GPU work や所有を終了しない。

`GpuRenderGraphSubmissionException.Completion` は queue への引渡し後に失敗した可能性を表す。presentation 接続は Retire された target を、GPU と表示側の使用終了が証明されるまで維持する。失敗した内容を Present したり、未提出として Discard したりしない。

stage 0 の両 provider が扱う共通 package profile は `images.sampled` version 1、線形の `Rgba8Unorm`／`Bgra8Unorm`、Opaque／Premultiplied の準備済み画像である。CPU stride の 0 は tightly packed を表す。import は転送先と shader 読取りの用途を持つ。色変換、decode、任意の Buffer package profile は追加しない。

下位 ResourceManager の管理操作は caller が直列化する。package import の待機中に別の import、提出、pin／scope 作成、回収、provider 固有の manager 操作を重ねない。返却が必要な import 中 scope の Dispose は、provider が実 import の終了まで遅延する。通常の Submit 同士と終了は provider が調停し、GPU 使用終了まで CPU を待たせる要件にはしない。

## 段階 0 の範囲

共通機能、二系統の provider、ResourceManager 接続、Hosting、Clear／Copy／基本 Output と offscreen presentation の統合を実装する。実ウィンドウ／canvas と frame pacing、構造 cache、contributor、frame 外部 lease、内部内容世代 cache、alias allocation の最適化、Model／2D は後続段階とする。

旧低レベル RenderGraph と旧描画系は削除済みであり、互換層はない。詳細な目標契約と実装済みの範囲は [ADR 0030](../../../docs/adr/0030-render-graph-api.md) と [実装進捗](../../../docs/designs/graphics-implementation-progress.md) を参照する。
