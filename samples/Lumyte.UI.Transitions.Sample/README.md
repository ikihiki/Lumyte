# UI transitions sample

`dotnet run --project samples/Lumyte.UI.Transitions.Sample -c Release`で、通常のComposition、`isPresent`へのSignal引数、共有key付きDIページを実行します。Headlessでは画像captureを省略し、論理commit、`VisualCompletion`のSkipped結果、旧scopeの解放、Automationによる終了を確認します。同じsampleをstrict Native AOTでも実行します。

`TransitionScene.cs`はBrowser sampleと共有します。重なった子を持つPanelのenter/exit、Hosted Popupのexit、ページ間の半透明な共有要素を定義します。`ManualClock`と長い準備期限はGPU検証で時間を固定するための設定です。通常アプリは既定clockと既定の期限を使えます。利用者がruntimeやframe更新を管理する必要はありません。

```csharp
using static Lumyte.UI.Compose;

var shown = Signal.Create(true);
var content = Panel(isPresent: shown,
    transitions: new UiElementTransitions
    {
        Enter = FadeIn(Duration.FromSeconds(0.15)),
        Exit = FadeOut(Duration.FromSeconds(0.15)),
    })[Rectangle(width: 80, height: 40)];
```

`shown.Value = false`で入力を止めて退出し、終了後にlayoutから外れます。treeから直接削除した場合は即時detachです。ページ間の演出は`UiNavigationOptions.Transitions`で別に設定します。
