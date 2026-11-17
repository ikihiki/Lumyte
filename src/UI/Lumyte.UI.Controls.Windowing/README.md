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
