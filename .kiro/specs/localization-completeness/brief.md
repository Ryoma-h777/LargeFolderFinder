# Brief: localization-completeness

## Problem

`LanguageKey` 列挙は80キーを定義しているが、**13言語のうち11言語は78キーしか持っていない**。欠落しているのは `HeaderOwner`（所有者カラムの見出し）と `ContextShowOwner`（右クリックメニューの項目）で、en と ja 以外のすべての言語で欠落している。

`GetText` は見つからないキーを英語にフォールバックするため例外にはならないが、**11言語で該当箇所だけ英語が表示される**。多言語対応を特徴として掲げている以上、これは品質上の欠陥である。

さらに深刻なのは、**この種の欠落が構造的に発覚しない**ことである。キーを追加しても13言語すべてに反映したかを検証する手段がなく、静かに英語へ落ちるだけなので、テストにも目視にも引っかからない。今後 UI を改修してラベルを増やすたびに同じことが起きる。

## Current State

- `Services/LocalizationManager.cs` の `LanguageKey` 列挙: 80キー
- `Resources/Languages/*.yaml`: en と ja が80キー、他11言語（de, es, fr, hi, it, ko, pt-BR, ru, tr, zh-CN, zh-TW）が78キー
- `GetText(LanguageKey)` は `key.ToString()` を文字列キーとして辞書引きし、失敗すると `en.yaml` にフォールバックする（順序には依存しない）
- ただし `LanguageKey` の宣言コメントには「YAML ファイルと順序を一致させること」と書かれており、**実装と食い違っている**
- 網羅性を検証する仕組みは存在しない

## Desired Outcome

- 13言語すべてが `LanguageKey` の全キーを持っている
- **キーの欠落が機械的に検出される**。新しいキーを追加して一部の言語に反映し忘れた場合、ビルドまたはテストで気づける
- `LanguageKey` の宣言コメントが実装と一致している

## Approach

2段構えとする。

1. **欠落の解消**: 11言語に `HeaderOwner` と `ContextShowOwner` を追加する。翻訳は各言語の既存の語彙・語調に合わせる（他のカラム見出しやメニュー項目の訳語と整合させること）
2. **再発の防止**: `LanguageKey` の全要素が全13言語の YAML に存在することを検証する仕組みを設ける。テストプロジェクトが整うまでは軽量な検証手段でよいが、**最終的には CI で自動実行されること**を目標とする

あわせて、実装と食い違っている宣言コメント（順序一致の指示）を修正する。

## Scope

- **In**:
  - 11言語への `HeaderOwner` / `ContextShowOwner` の追加
  - `LanguageKey` と全13言語 YAML の網羅性を検証する仕組み
  - `LanguageKey` の宣言コメントの修正（順序依存の記述を実態に合わせる）
  - 逆方向の検証（YAML にあって `LanguageKey` にない孤児キーの検出）

- **Out**:
  - 新しい UI ラベルの追加（`ui-redesign` の範囲）
  - 対応言語の追加・削除
  - ローカライズ機構そのものの作り替え（`LocalizationManager` のリファクタリングは `architecture-refactoring` の範囲）
  - 既存の訳文の品質向上・見直し

## Boundary Candidates

- 欠落キーの補完（データ作業）
- 網羅性検証の仕組み（コード作業）

## Out of Boundary

- 翻訳の外注や機械翻訳サービスの導入
- 言語切り替え UI の変更

## Upstream / Downstream

- **Upstream**: なし（他の作業と独立して着手できる）
- **Downstream**: `ui-redesign` がラベルを追加する際に、ここで整えた検証の仕組みを通すことが前提になる

## Constraints

- **依存なしで着手できる唯一の実コード修正**であり、.NET 10 移行の前後どちらでも成立すること。移行を待たずに進められるよう、移行後に捨てることになる実装を作らないこと
- 検証の仕組みは、テストプロジェクトが未整備の段階でも動く形にすること。移行後に xUnit v3 のテストへ引き上げる前提で設計する
- YAML の読み込みは YamlDotNet（`PascalCaseNamingConvention`）を使用している。既存の記法に合わせること
- 訳語は各言語の既存エントリと語調を揃えること。`HeaderOwner` は一覧のカラム見出し、`ContextShowOwner` は右クリックメニューの項目であり、それぞれ同種の既存キーが参考になる
