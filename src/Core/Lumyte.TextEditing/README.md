# Lumyte.TextEditing

UIに依存しないUTF-16文書と編集commandです。確定文字列、preedit、選択、属性を区別し、transactionのCommitで一度に公開します。Disposeだけなら未公開変更を破棄します。変換の取消は未確定区間だけをcheckpointへ戻し、途中確定を保持します。

```csharp
var document = new TextDocument("名前");
TextEditingCommands.ReplaceAll(document, "新しい名前", TextEditOrigin.Application);
TextDocumentSnapshot value = document.CaptureSnapshot();
```

位置はUTF-16 code unitで、surrogate途中の選択・置換を拒否します。削除はgrapheme単位です。CR/LFは単一行用の空白へ正規化し、Unicode正規化はしません。文書は既定32768 code unit（構築時に1〜1048576へ設定可能）、preedit属性は256件です。利用側は編集とsnapshot取得を同じ実行系列で呼びます。
