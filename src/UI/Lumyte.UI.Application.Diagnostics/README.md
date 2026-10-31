# UI Application Diagnostics

`UseDiagnostics` adds a scoped automation execution point to the application owner. It does not add a transport or open a socket. All input, text inspection and screenshots require explicit target permissions. Configure a transport separately using the existing Diagnostics registrations.

画面を交換するslotは、候補Targetを非公開で予約し、既存Jobの清掃後に同じ登録枠で交換します。現在Targetが容量上限に達していても置換できます。旧Targetの制御権は引き継がず、操作側は新Targetを取り直します。

Navigation専用metadataは任意の[Application.Navigation.Diagnostics](../Lumyte.UI.Application.Navigation.Diagnostics/README.md)へ分離しています。基本の`UseDiagnostics`だけでは`ui.navigation/state`を登録しません。
