# Lumyte.Diagnostics.RenderGraph

RenderGraph の CPU 集計と要求時の構造 capture を既存 `Lumyte.Diagnostics` に接続します。既存の catalog、execution point、HTTP／MagicOnion と telemetry collector を使用します。

```csharp
using Lumyte.Diagnostics;
using Lumyte.Diagnostics.RenderGraph;
using Lumyte.Graphics.RenderGraph;
using Microsoft.Extensions.DependencyInjection;

services.AddLumyteDiagnostics(options =>
{
    options.Enabled = true;
    options.AllowedMeterNames = [RenderGraphDiagnostics.TelemetryName];
    options.AllowedActivitySourceNames = [RenderGraphDiagnostics.TelemetryName];
    options.AllowedLogCategoryPrefixes = [RenderGraphDiagnostics.TelemetryName];
});
services.AddDiagnosticExecutionPoint<BeforeRender>("graphics.before-render");
services.AddRenderGraphDiagnostics<BeforeRender>(configure: options =>
{
    options.Enabled = true;
    options.MetricInterval = TimeSpan.FromSeconds(1);
    options.MaxCaptures = 2;
    options.MaxSnapshotBytes = 4 * 1024 * 1024;
    options.MaxRetainedBytes = 8 * 1024 * 1024;
    options.Lifetime = TimeSpan.FromSeconds(60);
});

// gameScope は既存の game 実行 scope。BeforeRender はその owner thread の marker。
RenderGraphDiagnostics observer = gameScope.ServiceProvider.GetRequiredService<RenderGraphDiagnostics>();
var graph = new RenderGraph(device, "main-frame", observer);
// pass／resource を宣言してから、アプリケーション側の frame 番号を任意で渡す。
using CompiledRenderGraph plan = graph.Compile(frameId: 42);
```

既存 point が登録済みなら `AddDiagnosticExecutionPoint` は重ねません。adapter は Scoped で、graph と diagnostic pump が同じ instance を使います。Compile／Record と pump は同じ owner thread で実行してください。pump の Activate／Pump／Deactivate、collector の Start と endpoint 登録は既存診断のライフサイクルに従います。

`DiagnosticOptions.Enabled` と `RenderGraphDiagnosticOptions.Enabled` が両方 true の場合に有効です。診断無効時は core が計時・通知・trace scope を作らず、明示 capture がない限り詳細 snapshot も作りません。有効時の通常経路も固定サイズの集計だけを更新し、毎 pass のイベントや毎 frame の snapshot は生成しません。

## 単発capture

すべて既存診断 module `graphics.render-graph` の Observe 権限で実行します。

| Operation | 引数 | 内容 |
| --- | --- | --- |
| `get-summary` | なし | 成功 compile／record と失敗の累積件数、CPU 時間の合計・最大値、pass／culling／barrier 合計、保持状況 |
| `request-capture` | `graph-name` | 次に Compile する指定名の graph を一回だけ選び、`capture-id` と pending 状態を返す。空文字は任意 graph |
| `get-capture` | `capture-id` | 対象 graph／run／frame、状態、件数、CPU 時間、推定保持 byte 数 |
| `get-pass` | `capture-id`, `index` | 追加順 index から pass 名、ExecutionIndex、副作用有無 |
| `get-resource` | `capture-id`, `index` | resource 名、種別、temporary／output 有無、最初／最後の live 使用 index |
| `get-dependency` | `capture-id`, `index` | 実行順の Before／After index |
| `get-barrier` | `capture-id`, `index` | resource／pass index、texture state、前後の stage／access |

1. `request-capture` に `graph-name: "main-frame"` を送り、返った `capture-id` を保持します。
2. owner thread が対象 graph を Compile すると、その一回だけ上限付き snapshot を生成します。`get-capture` で状態を取得します。
3. 同じ `capture-id` と各 `index` で構造を列挙します。後続 frame が進んでも capture の構造は更新されず、ExpectedRevision は不要です。

GraphId は builder ごとに固定、RunId は Compile ごとに一意です。FrameId は `Compile(frameId: ...)` で渡した非負の番号で、0 は未指定です。pending 中は GraphId／RunId が空、FrameId が -1 で、対象が決まるとその run に固定されます。別 graph／run の記録や失敗はその capture の状態を変更しません。

状態は pending、capturing、compiled、recorded、compile-failed、record-failed、too-large です。recorded は command 記録の成功を表し、GPU 完了は表しません。Compile 後の構造は immutable で、native allocation と callback を保持せず、plan の Dispose 後にも取得できます。culled pass の ExecutionIndex と未使用 resource の FirstPass／LastPass は -1、export barrier の PassIndex は live pass 数です。Resources／TransientResources は unused を含む宣言数です。

pending 要求は同時に一つで、既存要求が待機中なら busy、無効時は disabled を返します。詳細がまだなければ not-ready、推定 byte 上限超過なら too-large、期限切れ・退避・未知 ID と範囲外 index は not-found、schema の型・範囲違反は invalid-arguments です。

`MaxCaptures` は pending を含む保持数、`MaxSnapshotBytes` は一つの snapshot、`MaxRetainedBytes` は管理情報と Compile 中の予約を含む全 capture の推定保持量を制限します。上限確保のため古い capture を退避しても、その ID を新しい内容へ再利用しません。`Lifetime` は要求時刻から数え、対象 graph の待機時間も含みます。推定量は managed 診断情報の予算で、GPU memory と実行 plan は含みません。予算を超えた場合も Compile 自体と実行 plan は利用できます。

手元だけで検査する場合は core の `Compile(frameId: 42, capture: new RenderGraphCaptureOptions { MaxBytes = 4 * 1024 * 1024 })` を使えます。未要求または上限超過では `plan.Snapshot` が null になります。この明示指定だけでは adapter の remote capture 履歴に追加されません。

## 集計とtelemetry

`Lumyte.Graphics.RenderGraph` という meter／activity source／log category を使い、`lumyte.instance.id` で game scope と関連付けます。前回出力から `MetricInterval` 以上経過した compile／record／failure 通知または `get-summary` で、前回からの集計値を出力します。毎操作に対する metric event は生成しません。Dispose 時には残った最終区間を一度だけ出力するため、その出力だけは interval の経過を待ちません。最終区間を受信するには、adapter を Dispose してから collector を停止します。

| Metric | 内容 |
| --- | --- |
| `rendergraph.operations` | 成功 compile／record 件数。`operation` tag で区別 |
| `rendergraph.cpu.total` | 成功した CPU 処理時間の合計（ms）。`operation` tag で区別 |
| `rendergraph.cpu.window.max` | 各出力期間の CPU 最大時間（ms）。個々の処理時間の分布ではない |
| `rendergraph.plan.total` | pass／culling／resource／temporary／dependency／barrier の累積件数。`kind` tag で区別 |
| `rendergraph.failures` | compile／record 失敗の正確な件数 |

ActivitySource の compile／record span は要求された capture 対象だけに生成し、graph.id／graph.run.id／graph.name／frame.id を付けます。通常実行では span と相関 scope を作りません。Error の structured exception log は interval あたり最大一件に制限し、graph／run／frame を含めます。ログが制限されても失敗件数は全件を集計します。

時間は CPU 時間です。GPU timestamp、resource 内容、replay、複数 graph／frame をまとめた capture は後続で設計します。公開 API と登録の詳細は [GRAPHICS-0015](../../../docs/adr/graphics/GRAPHICS-0015-render-graph.md)、基本設定は [Lumyte.Diagnostics](../Lumyte.Diagnostics/README.md) を参照してください。
