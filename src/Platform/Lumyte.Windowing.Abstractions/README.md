# Lumyte.Windowing.Abstractions

Native 表示先の `IWindow`、生成オプション、状態スナップショット、要求結果、識別子を提供する .NET 10 契約パッケージです。名前空間も `Lumyte.Windowing.Abstractions` とします。

各 Platform バックエンドが `IWindow` を直接実装します。Platform、Input、Graphics、Windowing の管理実装に依存しません。単一 Window でも契約を使用できます。旧 API への型転送や互換ラッパーはありません。

Native の要求と観測を区別し、要求結果は `RequestCompleted` で受け取ります。外部 Surface などが Native を借用する間は `RetainNativeTarget` を保持し、GPU / 外部資源の解放後に解除します。

[共通契約の設計](../../../docs/adr/window/WINDOW-0001-window-system.md#native-window-の共通契約)を参照してください。
