# Lumyte.Diagnostics.UI.Automation.Contracts

BCLだけに依存する、閉じたUI操作planと結果DTOです。Widget、UI、Graphics backend、Diagnostics transportは参照しません。source-generatedなUiAutomationJsonContextを使用します。

外側は既存Diagnostics protocol 2、planJsonは独立したschemaVersion 1です。camelCaseのkindはclick/focus/press/count/expect/snapshot/screenshot、roleはgroup/button、KeyはInputの正規名（例: Enter）、modifierはControl/Alt/Shift/Metaです。別のキー番号体系を作りません。

click/focus/press/count/expectのlocatorは1〜8段、snapshot/screenshotは空です。test-idはvalue、roleはrole/name/includeHiddenを持ちます。文字列は256文字、planは32768文字、深さは16まで。clickのpositionは有限かつ非負の論理offset、pressだけがkey/modifiersを持ち、expectのcount predicateだけが非負のcountを持ちます。

UiRemoteSchema.ParsePlan／SerializePlanは未知・重複・不適切な型のフィールド、未対応の値、非finite座標、無関係なフィールドを拒否します。配列をコピーし、modifier順序等を正規化するため、受信後の変更で受付済みpayloadが変わりません。

snapshotのUInt64 IDとSequenceはdecimal string、GuidはD形式、矩形はfiniteなJSON numberです。親IDとsurface identityを使い、UIの参照やdelegateをシリアライズしません。PNGとtraceは別のbyte artifactとして取得します。

[設計](../../../docs/adr/ui/UI-0010-diagnostics-ui-automation.md)

Schema v4 は `check` / `uncheck` / `select-option` と `optionKey`、選択部品のroleを追加します。v1–v3 は新しい操作・field・roleを受理しません。snapshotの nullable checked/selected、group IDのUInt64 decimal string、option/selected key は detached DTOへコピーします。

schemaVersion 1–3 の snapshot 応答は従来の18 node fieldだけを返します。C3のroleは none へ投影し、check/select-option capabilityを公開しません。schemaVersion 4 は null（混在・非該当）を含め選択の5 fieldを保持します。

## Schema 6の数値入力

set-valueは有限なnumericValue、set-rangeは有限で順序付きのnumericValue/numericUpperを要求する。numeric情報は実数の範囲、step、scalarまたは二値、本文を含まない妥当性を持つ。新field・role・commandはv6だけで使用でき、v1〜v3の18項目、v4/v5の23項目のsnapshotは専用projectionで固定する。既存Valueの0〜1契約は変更しない。

## 項目roleの版

schemaVersion 7はlistbox／listitem／comboboxの検索を追加します。通常入力に接続されたListBoxは既存select-optionを使い、ComboBoxはclick／pressと独立popup pageを使います。新しいfieldと数値commandを追加しません。v1–3の18、v4–5の23、v6の24 node fieldは保持し、新roleをnoneへ投影します。v6の数値情報は変更しません。
