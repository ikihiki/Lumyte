# Lumyte.UI.Controls.Windowing

CollectionsとUI.Windowingを参照する任意adapter。CollectionPopupHostは既存UiPopupManagerとUiPopupOwnerを借用し、ポップアップの表示方針、入力範囲、focus復帰、終了待ちを既存管理器へ委譲する。

applicationのroot構築時はPopupOwnerがまだ準備中なので、`new CollectionPopupHost(context.Popups, () => context.PopupOwner)`で所有者を遅延取得する。独立したmanagerを使う場合は既に登録済みのUiPopupOwnerを直接渡せる。adapterはmanagerやownerをDisposeしない。

## メニューと受動ツールチップ

MenuPopupHostも既存UiPopupManagerとownerを借用します。
アプリのroot構築時は次のようにownerの取得を既存managerの次のdrainまで遅らせます。

```csharp
var host = new MenuPopupHost(context.Popups, () => context.PopupOwner);
```

初期IsOpenがtrueのTooltipにも使えます。
入力中は要求の予約だけを作り、内容factoryの一回評価と全子孫の新規性検証をowner loopで行います。
失敗または取消した要求は既存menuを置き換えません。submenuの論理親はアンカーの実surfaceから解決します。
MenuItemのcommandは、命令的なchainを閉じるか、既存の宣言的PopupへItemInvokedのOnDismiss要求をqueueします。
既存CollectionPopupHostの契約と依存方向は維持します。

## 宣言的な Dialog と Submenu

`using static Lumyte.UI.Controls.Windowing.DialogCompose;` の Dialog は Windowing の非表示 portal 宣言を返します。通常の Application は自動で所有者 loop に接続し、Signal の IsOpen と最新 callback を使います。内容には Controls の DialogContent、または任意の通常 Widget を置けます。Automatic は現在 Hosted へ fallback し、NativeRequired は内容準備前に native-unavailable を返します。Native modal の OS activation を提供したとは扱いません。

MenuCompose.Submenu は既存 MenuItem を返す recipe です。key と新しい Menu root を返す factory を渡し、任意の表示ラベルは通常内容へ置きます。親 Menu の MenuPopupHost を借用し、独立した状態機械を追加しません。Dialog 内容の anchor は元のサービスを借用していても実際の論理所有者へ解決されます。

[公開例](../../../samples/Lumyte.UI.Dialogs.Sample/README.md)と[UI-0036](../../../docs/adr/ui/UI-0036-dialog-toast-and-recipes.md)に利用・所有・終了条件をまとめています。
