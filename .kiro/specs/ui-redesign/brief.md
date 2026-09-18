# Brief: ui-redesign

## Problem

開発者から「見た目がださい。もうちょっと見やすくしたい」という要望がある。実装を確認すると、見た目を整えることが**構造的に難しい状態**になっている。

- **縦横2つのレイアウト XAML が、計365行のうち125行しか差分がない**。約66%が重複コピーであり、片方だけ修正する事故が起きやすい（実際に「レイアウト変更時に結果が表示されない不具合」を過去に修正している）
- **色・フォント・サイズがすべて直書き**。`#0078D7`、`#ABADB3`、`#F5F5F7` などが XAML に散在し、`FontFamily="MS Gothic"`、`FontSize="16"` もハードコード
- `App.xaml` と `Views/Icons.xaml` に `ResourceDictionary` は存在するが、**デザイントークンとしては活用されていない**

つまり「1箇所直せば全体が変わる」構造になっておらず、見た目の改善を試みるたびに2つの XAML を手作業で同期させる必要がある。

## Current State

- `Views/Layouts/VerticalLayoutView.xaml`（180行）と `HorizontalLayoutView.xaml`（185行）
- 両者は `IMainLayoutView` インターフェースを実装し、`LayoutViewBase` が `FindName("要素名")` の文字列で XAML 要素を解決する
- 結果一覧は `ListView` で、`VirtualizingStackPanel.IsVirtualizing="True"` + `VirtualizationMode="Recycling"`、`AlternationCount="2"` による交互背景
- フォントサイズは利用者が変更可能（`AppSettings.FontSize`、既定16.0）
- `MS Gothic` は等幅表示のため意図的に選ばれている可能性が高い（`TextMeasurer` に `GetCharWidth_MSGothic` があり、罫線によるツリー表示と桁揃えに依存している）

## Desired Outcome

- 色・フォント・間隔がトークンとして一元管理され、**1箇所の変更が全体に反映される**
- 縦横レイアウトの重複が解消され、片方だけ直す事故が起きない
- 一覧の視認性が向上している（情報密度、コントラスト、行の識別しやすさ）
- 走査結果の表示性能が落ちていない

## Approach

デザインの前に構造を整える。順序が逆だと、重複した2つの XAML に対して見た目の変更を二重に適用することになる。

1. **デザイントークンの整備** — 色・フォント・サイズ・間隔を `ResourceDictionary` に集約する。ライトテーマのみか、ダークテーマも視野に入れるかを決める
2. **レイアウト重複の解消** — 共通部分を切り出し、縦横で本当に異なる部分だけを差分として持つ構造にする。`LayoutViewBase` の `FindName` 依存の見直しと密接に関わるため、あわせて設計する
3. **視認性の改善** — トークンが整った上で、コントラスト・情報密度・行の識別性を見直す

UI コンポーネントライブラリは**原則として導入しない**。理由は、(1) 色・フォントの整理はライブラリの有無に関わらず必要な作業であること、(2) この画面は大量行の `ListView` が主役であり汎用コンポーネント集の恩恵が小さいこと、(3) 重いテーマ装飾が仮想化の性能を落とすリスクがあり速度優先の方針と衝突すること、である。

ただし Fluent な外観を強く求める場合の選択肢として **WPF UI（lepoco/wpfui、MIT ライセンス）**が実用に足ることは確認済みである。導入する場合は `ThirdPartyNotices.txt` への記載が必要になる。

## Scope

- **In**:
  - デザイントークン（色・フォント・サイズ・間隔）の整備と `ResourceDictionary` への集約
  - 縦横レイアウト XAML の重複解消
  - `IMainLayoutView` / `LayoutViewBase` の見直し（`FindName` 依存を含む）
  - 一覧の視認性改善（コントラスト、情報密度、行の識別性）
  - 新規に追加した UI ラベルの13言語対応（`localization-completeness` で整備した網羅チェックを通すこと）
  - 既知の誤訳の修正候補: `pt-BR.yaml` の `RemainingTimeM` がスペイン語混じり（`Faltan` → `Faltam`）。`localization-completeness` のレビューで見つけたが、訳文の見直しは同スペックの範囲外だった
  - ダークテーマ対応の可否判断
  - スキャンの行と表示設定の行の区切りの復元（2026-09-17 利用者の判断）。過去の要望で、行の間に境界線を入れ、表示設定の行を低く詰める形で実装されていたが、その後のコード変更で線と縮小が失われた。`.kiro/steering/decisions.md` の「スキャンの行と表示設定の行を分ける」を読み、記録された意図に沿って今の画面構成（タブ、縦横レイアウト）に合わせて戻すこと

- **Out**:
  - 機能の追加・変更（表示できる情報の種類を増やすなど。上記の区切りの復元を除く）
  - 走査ロジックへの変更
  - UI コンポーネントライブラリの全面採用（採用する場合は別途判断が必要）

## Boundary Candidates

- デザイントークンの整備
- レイアウト重複の解消とビューの抽象化見直し
- 視認性の改善

## Out of Boundary

- 画面構成そのものの再設計（タブ構成、メニュー構成の変更）
- 新しいレイアウトモードの追加

## Upstream / Downstream

- **Upstream**: `architecture-refactoring`（整理された構造の上で作業する）、`localization-completeness`（ラベル追加時の網羅チェックを利用する）
- **Downstream**: なし（ロードマップの最終スペック）

## Constraints

- **仮想化設定を壊さないこと。** `VirtualizingStackPanel.IsVirtualizing="True"` と `VirtualizationMode="Recycling"` は数十万行の表示に必須であり、performance.md でも「外さないこと」と明記している。装飾のために `ItemTemplate` を重くしないこと
- **フィルタ入力の300msデバウンスを維持すること**
- **`MS Gothic` の等幅性に依存している可能性が高い。** `TextMeasurer.GetCharWidth_MSGothic` と罫線（`┣` `┗` `┃`）によるツリー表示、`SizeFormat = "{0, 10:N0} {1}"` の桁揃え、`BaseSizeLength = 9` の文字数基準がフォントに結びついている。**フォントを変更する場合はこれらへの影響を必ず検証すること**。クリップボード出力の見た目も同じ整形に依存している
- 利用者が変更できるフォントサイズ（既定16.0）を尊重すること。トークン化しても利用者設定が効かなくなってはならない
- 13言語すべてで表示が破綻しないこと。特にドイツ語・ロシア語は文字列が長くなりやすく、レイアウトが崩れやすい
- UI ライブラリを導入する場合、**無料かつ商用利用可能でライセンス上の問題がないこと**が絶対条件（WPF UI = MIT、MahApps.Metro / HandyControl / MaterialDesignInXaml も同系統の無料 OSS）。導入時は `ThirdPartyNotices.txt` への記載が必須
- 新規ラベルは13言語すべてに追加すること。英語フォールバックがあるため欠落しても動いてしまう点に注意
