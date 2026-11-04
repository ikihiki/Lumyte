# UI部品ADRの分類

`docs/adr/ui-components/` は、個別UI部品の利用方法、制約、描画、入力、状態と受け入れ条件を記録するカテゴリです。カテゴリコードは `UI-COMPONENTS`、カテゴリ内の番号は独立して採番します。

`docs/adr/ui/` はWidget/Element、layout規約、入力配送、Window統合などの部品横断の基盤判断を扱います。個別の部品動作と共通基盤を分け、同じAPI判断を両カテゴリの正本にしない方針です。カテゴリは文書の責務区分であり、assemblyやnamespaceを増やすものではありません。

## C1の個別設計

- [Padding](adr/ui-components/UI-COMPONENTS-0001-padding.md): withへ登録する余白修飾。
- [Border](adr/ui-components/UI-COMPONENTS-0002-border.md): withへ登録する枠修飾。
- [Align](adr/ui-components/UI-COMPONENTS-0003-align.md): withへ登録する整列修飾。
- [ConstrainedBox](adr/ui-components/UI-COMPONENTS-0004-constrained-box.md): withへ登録する追加制約。
- [SizedBox](adr/ui-components/UI-COMPONENTS-0005-sized-box.md): 0〜1 childを持つ寸法指定Widget。
- [Separator](adr/ui-components/UI-COMPONENTS-0006-separator.md): 操作を持たない区切り線Widget。

共通の修飾実行方式は[UI-0024](adr/ui/UI-0024-ordered-layout-and-decoration-modifiers.md)に置きます。Padding等4種類をWidget数へ数えない区分を維持し、上の6部品をすべてWidget型として追加する意味ではありません。

これらは設計文書です。状態欄の採用判断と、実装・受け入れ条件の検証完了は区別します。Gridの配置情報やGridCellの設計はこのC1の対象に含めません。
