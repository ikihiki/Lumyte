# Lumyte.Diagnostics.RenderGraph

RenderGraph の最新 snapshot と CPU telemetry を既存 `Lumyte.Diagnostics` に接続します。新しい通信経路は追加せず、既存の catalog、execution point、HTTP／MagicOnion と telemetry collector を使用します。

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
services.AddRenderGraphDiagnostics<BeforeRender>();

// gameScope は既存の game 実行 scope。BeforeRender はその owner thread の marker。
RenderGraphDiagnostics observer = gameScope.ServiceProvider.GetRequiredService<RenderGraphDiagnostics>();
var graph = new RenderGraph(device, "main-frame", observer);
```

既存 point が登録済みなら `AddDiagnosticExecutionPoint` は重ねません。`RenderGraphDiagnostics` は Scoped で、graph と diagnostic pump が同じ instance を使います。compile／record と pump を同じ owner thread で実行してください。pump の Activate／Pump／Deactivate、collector の Start と endpoint 登録は既存診断のライフサイクルに従います。診断無効時は observer を渡さず利用できます。

| Operation | 内容 |
| --- | --- |
| `get-summary` | 最新 graph 名、状態、live／culled pass 数、resource／dependency／barrier 数と CPU 時間 |
| `get-pass` | 追加順 index から pass 名、ExecutionIndex、副作用有無 |
| `get-resource` | resource 名、種別、temporary／output 有無、live pass の最初／最後の使用 index |
| `get-dependency` | 実行順の Before／After index |
| `get-barrier` | resource／pass index、texture state、前後の stage／access |

すべて Observe 権限のみです。summary の Revision を各 index operation の ExpectedRevision に指定し、更新後の conflict は summary から再取得します。未 compile は not-ready、不正 index は invalid-arguments／not-found。culled pass の ExecutionIndex と未使用 resource の FirstPass／LastPass は -1、export barrier の PassIndex は live pass 数です。

最新一つの detached snapshot だけを保持し、native allocation と callback は保持しません。plan の Dispose 後にも取得できます。複数 graph が同じ observer を共有すると最後の compile／record が対象になります。

`Lumyte.Graphics.RenderGraph` という meter／activity source／log category を使い、`lumyte.instance.id` で game scope と関連付けます。`rendergraph.cpu.duration` は compile／record の CPU 時間（ms）、`rendergraph.plan.count` は各種件数、`rendergraph.failures` は compile／record の例外件数です。GPU 時間は測りません。structured exception log は Error、compile の件数 log は Debug です。記録の成功は GPU 完了を意味しません。

公開 API と登録の詳細は [GRAPHICS-0015](../../../docs/adr/graphics/GRAPHICS-0015-render-graph.md)、基本設定は [Lumyte.Diagnostics](../Lumyte.Diagnostics/README.md) を参照してください。
