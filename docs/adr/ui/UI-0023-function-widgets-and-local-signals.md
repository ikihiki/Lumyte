# ADR-UI-0023: 静的な構築関数によるWidgetと共通Use基盤

- 状態: 採用・実装済み（PR 18）
- 日付: 2026-10-30（共通Use・context・Navigation接続の追記: 2026-10-31）
- 対応: 実装PR 18、前提: PR 15〜17、UI-0002・UI-0019

## 2026-10-31の責務境界更新

[UI-0002](UI-0002-widget-tree-and-composition.md)でWidget宣言とElement実体の分離を採用した。関数の引数はWidget、body評価・Use table・context依存はFunctionElementへ置く。静的factoryとUse系APIは維持する。
旧revisionの実行記録は履歴として扱い、以下のElement分離後の再検証記録を参照する。
最終API・公開順・移行完了条件はUI-0002を正本とする。

## 背景

アプリケーションの部品を作るたびにWidgetを継承し、プロパティ・子slot・状態引継ぎを実装する必要はない。既存の`using static`による通常の関数呼出しを保ち、関数の引数をパラメータ、戻り値をUIツリーにしたい。同時に、再実行で関数内の状態を初期化し直してはならない。

## 決定

### 利用側API

既存の`public static partial Compose.Definitions`へ、`[Composable]`を付けたpublic static関数を書く。クラス方式のComposableも引き続き低水準部品の定義に使える。構築用context、runtime、購読開始・終了操作は引数に追加しない。

```csharp
using Lumyte.Composition;
using Lumyte.Signals;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Example;

public static partial class Compose
{
    public static partial class Definitions
    {
        [Composable]
        public static Widget Counter(string label, int start = 0,
            Action<int>? onChanged = null)
        {
            Signal<int> count = UseSignal(start);
            return Button(automationName: $"{label}: {count.Value}",
                onClick: () =>
                {
                    count.Value++;
                    onChanged?.Invoke(count.Peek());
                });
        }
    }
}
```

呼出側は`using static Example.Compose;`から`Counter(label: labelSignal, key: "counter")`と書く。定義関数の`label`はstringのままで、生成factoryが`UiArgument<string>`へ拡張する。定義型は従来どおり複数形の`Definitions`とする。別の外側クラス名は`Composable.Factory`または既存の`CompositionDefaults`で指定できる。

- 関数の引数名・順序・型・既定値を生成factoryへ反映する。省略時には定義した既定値を使う。具体型として渡したnull/defaultは指定値として扱い、adapter自体のdefaultは未指定を意味する。必要なら`UiArgument<T>.Constant(default)`で明示する。必須値の未指定adapterは初期値取得時に拒否する。
- 具体的delegateはcallbackとして直接受け取り、lambdaやmethod groupにcastを要求しない。関数本体を実行しただけではcallbackを呼ばない。
- 値引数は定数・Signal・Computed・UiAnimatedを受け取る。関数には現在の具体的な値を渡す。アニメーション状態の継承で値が変わった場合も、既に生成したbodyの引数だけを差し替えず、通常更新境界で関数を再実行してから表示する。関数が直接読み取ったSignalもUIが購読する。
- 生成factoryには最後に`string? key = null`を追加する。`key`および`__`から始まる引数名は予約する。
- 戻り値はWidget、またはWidgetの派生型。生成factoryの戻り値はWidgetであり、内部wrapperの実装型を要求しない。子を受け取る部品は`Func<Widget>`等を関数の引数に宣言し、関数内で新しい子ツリーを作る。所有済みWidgetを再利用しない。
- generic関数と公開型制約をサポートする。async、ref戻り値、ref/out/in、params、ref struct、非公開の引数型はLYC001で拒否する。同名factoryの重複は拒否し、overloadには`Composable.Name`で別名を付ける。

### 実行と状態の照合

生成factoryは、関数ごとに異なる具体型の宣言wrapperを返す。`UiSurface`は対応するFunctionElementの準備処理で関数を評価し、戻り値を候補の子宣言として受け取る。wrapperは引数と呼出情報だけを持ち、bodyの実行時slotとUse tableはFunctionElementへ置く。直接`Definitions.Counter(...)`を呼ぶ方法は利用側factoryではなく、ローカル状態の境界を作らない。

新しいwrapperを評価する前に、現在表示中のツリーと親・子slot・型・Keyで照合する。Keyなしは位置と型で照合する。対応するFunctionElementのUse tableから候補を準備して関数を再実行し、返された子ツリーも同じ規則で照合する。Key付きの兄弟の並べ替えと入れ子でも状態を保つ。関数の種類・Key・親slotが変わった場合、または削除を適用した後で再追加した場合は初期化する。

関数bodyは再実行ごとに新しいツリーを返す。動的な子・条件分岐はbody内に普通のC#として書く。body内の各ノードへ生成factoryを要求せず、関数wrapperを再構築recipeの不透明な境界として扱う。外側の手書きWidgetに生成factoryがない場合は、従来どおり`new UiSurface(() => Build())`を使用する。

Signalの変化は既存surfaceの再構築経路へ合流する。この実装は関数ごとの部分更新schedulerを追加せず、必要時に宣言を再構築し、既存のlayout・入力・描画の再利用判定を使う。

### ローカルSignal

`using static Lumyte.UI.Compose;`で`UseSignal(initialValue)`または`UseSignal(() => CreateInitialValue())`を使う。戻り値は独立したSignalsライブラリの`Signal<T>`であり、UI専用のSignal型やBindを作らない。

- 初期値は同じ関数インスタンス・呼出位置の初回だけ使用する。factory版の初期化関数も初回だけ評価する。後続のパラメータ変更で初期値を上書きしない。
- compilerが渡すファイル・行と、その位置での呼出出現番号でローカル値を識別する。異なる行の条件付き呼出しを追加しても別の呼出位置の状態をずらさない。同一行には複数のUseSignal呼出しを書かない。
- 同じ位置でループする場合は`key:`へ各要素の安定した識別子を渡せる。省略すると同じ位置内の呼出順になる。子コンポーネントの並べ替えには、生成factoryのKeyを使う。直前の採用buildに存在しなかった呼出位置は再初期化する。
- UIが同期的な関数評価中だけ内部contextを設定し、例外時にも必ず復元する。利用者のEnterBuildやthreadへの固定は不要。処理が重ならなければ別threadで次の再構築を実行できる。
- 関数評価外のUseSignalは明示的に失敗する。通常の`Signal.Create`は従来どおり使えるが、関数内で毎回呼ぶと新しい値になる。外部のモデルSignalはそのまま関数から読み取れる。
- UseSignalは共通Use基盤へSignalの初回生成を委譲する。Signal自体の所有・処理系をUIへ移さない。Effectや外部資源は既存のページcontextや明示的ownerの寿命で管理する。

### 共通Use基盤と実行フレーム

`Use<T>(Func<T> initialize, string? key = null)`を、関数インスタンス内で値を保持する共通入口とする。`UseSignal`もこの入口から`Signal.Create`を初回だけ呼ぶ。呼出位置・任意key・同位置の出現番号という識別規則は共通で、nullを含む任意の値を保持する。保持値の型を同じ呼出位置で変更した場合は失敗する。`Use`はEffectの開始やIDisposableの自動破棄を行わず、利用者が持つ資源の所有権を引き取らない。

`UiSurface`はFunctionElementを照合し、親Elementのcontextと新しい宣言の提供値を解決してから、FunctionElementが新しい引数でbodyを評価する。呼出し直前に内部のthread-localな実行フレームを設定し、`finally`で直前のフレームへ戻す。フレームには状態表とcontextだけを持たせ、Navigation固有の型をUI本体へ追加しない。非同期処理へフレームを持ち出さず、別rootを同期的に構築する再入でも独立したフレームを使う。利用者がEnterBuild等を管理する必要はない。

`UiContextKey<T>`は提供値の型付きidentityであり、同じTでも別のkeyは独立する。`Provide(context, value, content)`は新しい宣言に借用値を付加し、同じ宣言を返す。表示・所有slot・layoutに追加のwrapperを作らない。`UseContext(context)`は評価中の関数のcontextから最も近い提供値を返す。未提供の場合と評価外の呼出しは明示的に失敗する。

```csharp
// アプリが共有するkey。Tが同じでも別のkeyとは混ざらない。
public static readonly UiContextKey<string> Theme = new();

// [Composable]な定義関数内。
string theme = UseContext(Theme);
var cache = Use(() => new Dictionary<string, string>());

// 単独のrootを構築する側。ReaderはThemeを読む生成factory。
using var surface = new UiSurface(Provide(Theme, "dark", Reader()));
```

通常ノードを挟んでもcontextを子へ伝え、部分木のProvideはその部分木だけを上書きする。同じ宣言に`Provide(key, outer, Provide(key, inner, child))`と重ねた場合もinnerを優先する。recipeは各宣言の提供情報も保持するため、Signal更新後の新しい宣言から提供値が消えることはない。明示的なRebuildで異なる提供値を使う場合は、その値を付加した新しい宣言を渡す。Provideは表示中の既存宣言を動的に書き換えるAPIではない。

contextはSurfaceごとのツリー走査で明示的に引き継ぐ。portal先・別Surface・別Windowへ実行中のambient値を暗黙に持ち出さず、先のhostで必要な値を提供する。子を変更する場合は新しい宣言を構築し、公開予定の親Elementのcontextから子の関数を評価する。既存宣言のcollectionを初期化しても実行中のツリーは変更しない。context値もUseの保持値もstaticなglobal registryへ登録しない。

### 関数の入れ子と単独実行

| 呼び出し方 | 評価と状態の境界 |
| --- | --- |
| 生成factoryを単独のSurface rootにする | Surfaceがフレームを設定してbodyを評価する。Useはその関数の状態を持つ。必要なcontextはrootへProvideする。 |
| 関数のbodyから別の生成factoryを返す | 親のbodyでは子の宣言だけを作る。続くツリー走査で、子専用の状態表と親からのcontextを設定して評価する。 |
| 関数のbodyからDefinitionsの関数を直接呼ぶ | 通常の同期helperとして親のフレームを共有する。独立した関数identityを作らない。 |
| host外でDefinitionsの関数を直接呼ぶ | 通常のC#呼出しになる。内部でUse系を呼ぶと、評価フレームがないため失敗する。 |

生成factoryの呼出しだけではUseは実行されない。Surfaceがbodyを評価する時点で有効になる。イベントcallbackではbody内で取得した値・navigatorをcaptureし、callback内からUse系を呼び直さない。

### 任意Navigationの接続

任意の`Lumyte.UI.Navigation`へ`UiNavigationHooks.UseNavigation()`と`ProvideNavigation(navigation, content)`を置く。内部で専用の`UiContextKey<IUiNavigator>`を共通UseContext/Provideへ接続する。UI本体とCompositionはNavigationへ依存しない。

DIページを構築する`UiPageContext`は、そのactivationのnavigatorを返すrootへ自動的に提供する。したがってページ内の入れ子の関数へcontextやnavigatorを引数で転送する必要はない。DIなしのactivation providerや独立したSurfaceでは、hostが`ProvideNavigation`を使う。Windowごとに提供したnavigatorを借用し、子で明示的に上書きした場合はその値を使う。取得だけでは購読や遷移は開始せず、既存のページ・Windowの停止と退役規則を維持する。

```csharp
using static Lumyte.UI.Compose;
using static Lumyte.UI.Navigation.UiNavigationHooks;

// [Composable]な定義関数内。ページhostから自動提供される。
var navigation = UseNavigation();
return Button(onClick: () => navigation.RequestNavigate("/details"));
```

```csharp
// 独立したhostから、生成factoryのHomeを単独表示する場合。
using var surface = new UiSurface(ProvideNavigation(navigation, Home()));
```

遷移先の準備・guard・commit・旧ページの退役は既存Navigatorへ委譲する。再実行されるbodyの途中で遷移を開始せず、イベント等から要求する。既存のAction引数による操作の受け渡しも維持する。

### 購読・失敗・寿命

各bodyの直接読取をSignalReadSetで取得し、そのbuildの依存関係を既存のsurface購読へ登録する。分岐が変われば購読対象も変わる。bodyがSignalを子factoryへ渡すだけの場合は、既存のUiArgument購読が更新を検出する。UIが内部scopeを管理し、明示的な購読登録は不要。

準備では旧状態の格納表を書き換えず、新しい宣言に使用したSignal参照だけを保存する。関数・子所有関係・重複Key・Signal準備が失敗しても表示中のrootを交換しない。準備の副作用やSignalへの明示的な書込みを巻き戻す契約は作らず、bodyは状態の読取りとUI構築に使い、値の更新はcallbackで行う。非同期処理や再構築中の並行更新も追加しない。

削除・置換・Disposeでは既存のsurface購読を解除する。ローカル値のグローバルcacheは持たず、破棄した宣言への参照がなくなれば回収できる。外部へ渡したSignalは独立した値として利用できるが、切断済みUIを再構築しない。

### CompositionとUIの依存方向

汎用の属性・関数factory・wrapper生成・診断はmain向けの独立PRで提供する[COMPOSITION-0005](../composition/COMPOSITION-0005-function-factories.md)を使う。UIのWidgetがComposeFunctionAdapterでUiFunctionWidgetを指定し、UIが関数の実行と状態保持を担当する。本PRにはCompositionの契約・Generator・汎用テストの変更を含めない。

CompositionはUIやSignalsを参照しない。利用者に内部wrapper型・runtime・reflectionによる呼出しを要求せず、生成された型付きC#をNativeAOTへ渡す。

## 配置と検証

- 前提のComposition独立PR: 属性のMethod対応、関数factory・wrapper生成、既定値・delegate・generic・LYC001のテスト。本PRでは再実装しない。
- UI: UiFunctionWidget、共通Use/UseContext/Provide、UseSignal、関数展開、再構築recipe境界。既存クラス方式との混在、内部状態、Keyの並べ替え・変更・削除、条件依存、失敗後の再構築、入力callback、親子context・近い提供元・再入時の復元・別rootの分離・再構築したslotのcontextをテスト。
- NavigationとApplication.Navigation: 任意のUseNavigation/ProvideNavigationとページの自動提供を接続する。ページrootの単独関数、入れ子の関数、明示提供した独立Surfaceと、関数のイベントからの遷移を実hostで検証する。
- [実行サンプル](../../../samples/Lumyte.UI.Functions.Sample/README.md): OS/GPUなしで通常入力とSignalによる再構築を検証し、TestSignalsとstrict NativeAOT検証へ接続する。

## 代案と結果

直接静的関数を毎回呼ぶだけでは、関数内のSignalと子の論理identityを照合する前に状態を失う。利用者にcontextやscopeを渡させる方式は、通常の関数呼出しというAPI要件に合わない。生成wrapperで境界を作り、保持する値だけをUIが管理する方式を採用する。

低水準部品はWidgetクラスで設定を宣言し、型付きElementで描画や入力を実装する。アプリケーションの組合せ部品は静的関数だけで書ける。Effect hook、hot reloadでの行番号移動、非同期componentはこのPRに含めない。関数境界の部分更新は以下で定義する。

## 共通の部分木更新への接続

Widget共通の交換・検証契約は最初の実装PR #77の[UI-0002](UI-0002-widget-tree-and-composition.md)、Widgetごとの購読・部分公開はPR #93の[UI-0019](UI-0019-signals-and-reactive-state.md)で実装する。本PRで別schedulerや専用の子交換基盤を作らない。

関数wrapperはRebuildsChildrenをtrueにし、引数またはbodyの直接読取が変わったときbodyから子ツリーを新しく構築する。通常Widgetが未初期化slotへ旧内容を保持する場合と異なり、再生成する子のdirty更新は親の更新へ集約する。保持されたFunctionElementは不要に再評価せず、購読とUseの状態を継続する。

部分更新でも、FunctionElementが親contextと前回のUse tableから候補を準備してbodyを実行する。関数bodyの直接読取はElementが保持するSignalReadSetとして既存のobserverへ登録し、Element共通の照合・検証・入力取消・公開を通す。関数固有の差分は、body評価、Useとcontext、子を再構築する境界だけとする。

## 分離後の状態と破棄

FunctionElementがUse tableとcontext依存を継続保持し、新しいwrapperへ状態をコピーしない。
候補buildで追加したUse値は候補が所有し、失敗時に既存tableへ混ぜない。
公開時だけ候補tableを採用する。関数の種類・key・親slot変更と除去は実体の寿命を終了する。
Use系の呼出位置の照合、入れ子・別root・別threadの内部フレーム分離は維持する。
静的factoryの戻り値は引数だけを持つUiFunctionWidgetであり、結果の現在値を診断する場合はUiFunctionElement.Content.Widgetを参照する。

## 構築と公開の具体的な順序

1. 共通RebuildPlanが型・key・親slotでElementを照合する。
2. 引数のanimation leaseを準備し、引き継いだ現在値から新しい宣言を再生成する。
3. 共通Element hookのBuildContentへ解決済み宣言とUiElementBuildContextを渡す。
   UiFunctionElementは既存Use tableを参照して候補frameを作り、bodyを評価する。
4. UiElementContentの子宣言を共通の照合・投影検証へ接続する。
   body内で新しく作った空collectionは空として扱い、削除した子を保持しない。
5. 全候補の検証後にElementの設定と子の対応を公開し、候補Use tableを採用する。
   observerがbodyの読取依存を引き継ぐ。例外があれば未公開のleaseを解放する。

UiElementContentはWidget側のcollectionへ保存しない。同じ関数宣言を二つのSurfaceへ渡しても、
Use table・bodyのElement・購読はSurfaceごとに独立する。手書きElementもBuildContentをoverrideでき、
新しい状態の採用はUiElementContentのonCommittedへ置ける。準備中に既存状態や外部資源を変更しない。
Useで返した任意のIDisposableを自動Disposeする契約は追加しない。

2026-10-31のElement移行後はRelease UI 300件、ページ・Navigation連携27件と関数サンプルを確認した。
同一宣言の複数SurfaceでのUse状態分離、入れ子Surfaceの依存分離、空になった子の購読解除を含む。
