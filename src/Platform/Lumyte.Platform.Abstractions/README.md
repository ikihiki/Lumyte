# Lumyte.Platform.Abstractions

`IPlatform` と Clipboard・文字入力の共通契約を提供する .NET 10 パッケージです。

Platform は `Lumyte.Windowing.Abstractions` の `IWindow` と、`Lumyte.Input.Abstractions` の `IInputSource` を生成します。InputSystem と Windowing 管理実装は依存に含めません。

`PumpEvents` を OS イベント取得の唯一の入口とし、その後に任意の WindowSystem と InputSystem を更新します。サービスの有無は `Features` で確認してください。利用する OS がサービスを実装していない場合、factory は `NotSupportedException` を送出します。

文字入力では UI が `ITextInputClient` を実装して UTF-16 文書、選択範囲、Native クライアントの物理ピクセルでの入力位置を提供します。Clipboard は非同期 API とし、権限拒否・使用中・Native エラーを利用側へ伝えます。

- [生成と更新の設計](../../../docs/adr/platform/PLATFORM-0001-platform-and-window-contracts.md)
- [Clipboard と文字入力](../../../docs/adr/platform/PLATFORM-0002-clipboard-and-text-input.md)
