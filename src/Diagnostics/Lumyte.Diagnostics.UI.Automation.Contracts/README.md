# Lumyte.Diagnostics.UI.Automation.Contracts

BCLだけに依存する、閉じたUI操作planと結果DTOです。Widget、UI、Graphics backend、Diagnostics transportは参照しません。source-generatedなUiAutomationJsonContextを使用します。

外側は既存Diagnostics protocol 2、planJsonは独立したschemaVersion 1です。camelCaseのkindはclick/focus/press/count/expect/snapshot/screenshot、roleはgroup/button、KeyはInputの正規名（例: Enter）、modifierはControl/Alt/Shift/Metaです。別のキー番号体系を作りません。

click/focus/press/count/expectのlocatorは1〜8段、snapshot/screenshotは空です。test-idはvalue、roleはrole/name/includeHiddenを持ちます。文字列は256文字、planは32768文字、深さは16まで。clickのpositionは有限かつ非負の論理offset、pressだけがkey/modifiersを持ち、expectのcount predicateだけが非負のcountを持ちます。

UiRemoteSchema.ParsePlan／SerializePlanは未知・重複・不適切な型のフィールド、未対応の値、非finite座標、無関係なフィールドを拒否します。配列をコピーし、modifier順序等を正規化するため、受信後の変更で受付済みpayloadが変わりません。

snapshotのUInt64 IDとSequenceはdecimal string、GuidはD形式、矩形はfiniteなJSON numberです。親IDとsurface identityを使い、UIの参照やdelegateをシリアライズしません。PNGとtraceは別のbyte artifactとして取得します。

[設計](../../../docs/adr/ui/UI-0010-diagnostics-ui-automation.md)
