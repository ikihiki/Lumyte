# Navigation sample

`dotnet run --project samples/Lumyte.UI.Navigation.Sample -c Release`で、DIを使わないproviderと標準Headless hostによるページ切替を実行します。AutomationでButtonを操作し、型付き引数、同じWindow内でのSurface交換、終了を検査します。

`ExamplePages.cs`はページproviderの実装例です。Native AOTでも同じ実行経路を検証します。

標準hostとの接続には任意の`Lumyte.UI.Application.Navigation`を明示参照し、`using Lumyte.UI.Application.Navigation;`を追加します。通常のApplication／driverへNavigation依存を追加しません。
