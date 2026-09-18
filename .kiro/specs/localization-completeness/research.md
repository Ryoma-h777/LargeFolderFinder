# Research & Design Decisions

## Summary
- **Feature**: `localization-completeness`
- **Discovery Scope**: Extension（既存のローカライズ機構と言語ファイルに、データの修正と独立した検証ツールを加える）
- **Key Findings**:
  - アプリの読み込み方（YamlDotNet 16.3.0 の `DeserializerBuilder().Build()` で `Dictionary<string, string>` へ読む）では、**重複キーは例外にならず後の値で黙って上書きされる**。構文の誤りと入れ子の構造は例外、空のファイルは null になり、その言語全体が英語に置き換わる
  - 11言語の `TimeStatusFormat` は `"{0} <経過の語>"` で、`ElapsedFormat` がすでに経過の語を付けているため、**「経過」が二重に出たうえで残り時間が表示されない**。en と ja の `"{0} {1}"` にそろえれば両方が解消する
  - 既存の見出しの訳語（ja「名前・サイズ・種類・更新日時」、ko「수정 날짜」、zh-CN「修改日期」など）は Windows エクスプローラーの列名と一致しており、所有者の見出しもエクスプローラーの列名に合わせるのが既存の語調と整合する

## Research Log

### 言語ファイルの実態（2026-09-17）
- **Context**: brief の数値（80キー、78キー）が現状と合うかを確かめるため
- **Sources Consulted**: `Services/LocalizationManager.cs`、`Resources/Languages/*.yaml`（Python による集計）
- **Findings**:
  - `LanguageKey` は81キー。en と ja は81キー、他の11言語は79キーで、欠落は `HeaderOwner` と `ContextShowOwner` のみ。孤児キー、重複キー、空の訳文は0件
  - 差し込み位置の番号の組が en と異なるのは、11言語の `TimeStatusFormat` と、ja・ru の `LiveScanningMessage`（ja は `{0}` を持たず、ru は `{0}` の代わりに「ГБ」を直書き）。`LiveScanningMessage` はコードから参照されていない
  - アプリは言語フォルダ直下の `*.yaml` だけを列挙する（`Directory.GetFiles(dir, "*.yaml")`、下位フォルダは見ない）
- **Implications**: 検証ツールは修正前のデータに対して「欠落22件、差し込み位置13件」を報告するはずで、これが検出能力の実データでの証明になる

### YamlDotNet の読み込みの挙動（実測）
- **Context**: 要件3.5の「読み込めない」の範囲を、アプリと同じ規則で確かめるため
- **Sources Consulted**: NuGet キャッシュの YamlDotNet 16.3.0（net47）を PowerShell から読み、アプリと同じ呼び出しで試した
- **Findings**:
  - 重複キー（`A: "1"` と `A: "2"`）→ 成功し `A=2`
  - 構文の誤り → `YamlDotNet.Core.SyntaxErrorException`
  - 入れ子の構造 → `YamlDotNet.Core.YamlException`
  - 空のファイル、コメントだけのファイル → 例外なしで null（アプリは「dict is null」を記録して、その言語を失敗扱いにする）
  - 数値の値（`A: 12`）→ 文字列 `"12"` として成功
  - **表現モデル（`YamlStream.Load`）は、最上位に重複キーがあると `YamlException("Duplicate key A")` を投げる**（2026-09-18、実装中に判明して実測。当初の設計はこれを使う前提だったため誤りだった）
- **Implications**: 重複は「読み込み不能」ではなく別の問題として、辞書への読み込みとは別の経路（解析器の事象の列）で検出する必要がある。要件3.5を事実に合わせて改め、3.10 を追加した

### 既存の検証ツールの型
- **Context**: 新しい検証をどこに置くかを決めるため
- **Sources Consulted**: `Tools/GoldenBaseline/`（csproj、`Program.cs`、`SelfCheck/`）
- **Findings**: net48 のコンソールアプリで、アプリ本体を `ProjectReference` で読み取り専用に参照する。終了コードは 0（一致）、1（差異）、2（エラー）。テスト基盤がないため `selfcheck` サブコマンドで自己検証項目をまとめて実行する。ビルドは `-p:BuildProjectReferences=false` でアプリ本体を再ビルドしない運用
- **Implications**: 同じ型に合わせれば、開発者の操作と CI からの判定方法が統一される

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| 独立したコンソールツール `Tools/LocalizationCheck` | GoldenBaseline と同じ型。アプリを参照して `LanguageKey` を得る | アプリのビルドに影響しない。キー一覧を C# の解析なしで得られる。終了コードで CI から判定できる | ツールのビルドにアプリのビルド出力が要る | 採用 |
| GoldenBaseline のサブコマンドに追加 | 既存ツールに `localization` を足す | プロジェクトが増えない | GoldenBaseline の責務（走査結果の比較）を越え、境界が混ざる | 不採用 |
| アプリのビルドに組み込む（MSBuild のターゲット） | ビルド時に検証する | 忘れない | 利用者の決定（ビルドを止めない）に反する | 不採用 |
| PowerShell スクリプト | ソースの enum と YAML を文字列で解析 | ビルド不要 | アプリと同じ読み込み規則を再現できない。xUnit へ引き上げられない | 不採用 |

## Design Decisions

### Decision: 判定の中核をコンソールに依存しない部品に分ける
- **Context**: 要件4.3（.NET 10 移行後も判定規則を作り直さない）。移行後は xUnit v3 のテストから同じ判定を呼びたい
- **Alternatives Considered**:
  1. `Program.cs` に判定を直接書く
  2. 判定を、入力（キー名の一覧と言語ファイルの内容）から結果（問題の一覧）を返す純粋な部品にし、`Program.cs` はファイルの列挙と表示だけを担う
- **Selected Approach**: 2
- **Rationale**: テストから呼ぶときにコンソールやファイルシステムを介さずに済む。移行時はツールの対象フレームワークを変えるだけでよい
- **Trade-offs**: ファイルが数個増える
- **Follow-up**: `dotnet10-migration` でテストプロジェクトから参照できることを確かめる

### Decision: アプリと同じ読み込み規則を検証側で再現する
- **Context**: 要件3.5。`LocalizationManager` の読み込み処理は private で、実行ファイルの場所に固定され、ログも書く
- **Alternatives Considered**:
  1. `LocalizationManager` を公開して呼ぶ（アプリのコードを変える）
  2. 検証側で同じ呼び出し（`new DeserializerBuilder().Build().Deserialize<Dictionary<string, string>>`）を行う
- **Selected Approach**: 2
- **Rationale**: 要件6.1（読み込みの挙動を変えない）と、機構の作り替えは `architecture-refactoring` の範囲という境界を守る
- **Trade-offs**: アプリの読み込み規則が変わると、検証側も追従が必要になる（Revalidation Trigger に記載）

### Decision: 重複キーは YAML の解析器の事象の列で検出する
- **Context**: 要件3.10。辞書への読み込みでは重複が黙って上書きされる
- **Selected Approach**: YamlDotNet の解析器が出す事象の列をたどり、最上位のマッピングのキーの出現回数を数える。値は入れ子ごと読み飛ばす。辞書への読み込み（読み込み可否の判定）とは別に行う
- **Rationale**: YamlDotNet の同梱機能だけで、アプリの読み込み規則を変えずに検出できる
- **当初の案とその不採用**（2026-09-18）: 表現モデル（`YamlStream`）で数える案を設計に書いていたが、`YamlStream.Load` は最上位に重複キーがあると例外になり、数える用途に使えないことが実装中の実測で判明したため、一段下の解析器の事象を使う形に改めた

### Decision: 所有者の訳語は Windows エクスプローラーの列名に合わせる
- **Context**: 要件1.4。既存の見出しの訳語はエクスプローラーの列名と一致している
- **Selected Approach**: `HeaderOwner` は各言語のエクスプローラーの「所有者」列の名前、`ContextShowOwner` は同じ語を使い、その言語の既存の右クリックメニュー項目と同じ語形（動詞の形、語順）で「所有者を表示」を表す
- **Follow-up**: 実装時に各言語の既存項目と並べて確かめる

## Risks & Mitigations
- 訳語が不自然になる（利用者にネイティブの確認手段がない） — エクスプローラーの列名という既存の公式な訳語に寄せ、語形は同じ言語の既存項目にそろえる
- 検証ツールのビルドにアプリのビルド出力が必要 — 使い方に「先にアプリを一度ビルドする」ことを明記する。CI では `dotnet10-migration` が順序を整える
- 検証側の読み込み規則がアプリとずれる — 規則を1か所にまとめ、`LocalizationManager` の読み込みが変わったら再検証する

## References
- `Services/LocalizationManager.cs` — `LanguageKey` と読み込み・置き換えの実装
- `Tools/GoldenBaseline/` — 既存の検証ツールの型（終了コード、selfcheck）
- `.kiro/steering/decisions.md` — 単位名を訳さない決定
