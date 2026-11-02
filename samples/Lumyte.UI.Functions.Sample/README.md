# Function Widget sample

`CounterUi.Definitions.Counter`へ静的関数だけを定義し、Generatorが外側の`CounterUi.Counter`を生成します。関数の引数はパラメータ、戻り値はUIツリー、`UseSignal`は再構築後も維持する内部状態です。

```csharp
using static Lumyte.UI.Functions.Sample.CounterUi;

var label = Signal.Create("Count");
using var surface = new UiSurface(Counter(label, key: "main"));
```

サンプルは通常のpointer入力で内部カウンターを更新し、引数のSignal変更・明示Rebuildで状態を維持し、Key変更で初期化することを確認します。追加のContextCounterで、共通Useの初回生成・Provide/UseContextによる提供値の更新・入れ子の関数への継承も確認します。OSウインドウ・GPU・独自runtimeは不要です。

```bash
dotnet run --project samples/Lumyte.UI.Functions.Sample -c Release
```

ソースからのビルドには既存Drawing2DのSlang環境が必要です。TestSignals、CIのSamplesとstrict NativeAOT対象へ登録しています。アプリで関数Widgetを定義するprojectには、Composition GeneratorをAnalyzerとして参照してください。

[設計と状態の規則](../../docs/adr/ui/UI-0023-function-widgets-and-local-signals.md)
