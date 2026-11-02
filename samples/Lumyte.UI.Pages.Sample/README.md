# DI pages sample

`dotnet run --project samples/Lumyte.UI.Pages.Sample -c Release`で、`MapPage`の関数ページから`MapGroup`内の型付きclassページへ遷移します。constructorへ同じactivationのcontextとScoped serviceを注入し、非同期準備後のUI構築、同じWindow内のSurface交換、旧scopeの解放と終了時の全scope一回破棄をAutomationクリックで検証します。

`ExamplePages.cs`が登録とCompositionの実装、`SampleState.cs`が検証用の共有状態です。`ObservedWindow`はsampleの検証用にWindow contextを借用する薄いwrapperで、通常アプリでは`UseNavigation("/")`または`content: Page(home)`だけで起動できます。Native AOTでも同じコードをpublishして実行します。

共有モデルの`Width` SignalをButtonとRectangleの引数へ直接渡します。通常クリックで値を更新してから遷移し、UI側が購読と旧surface解放を管理します。
