# Research & Design Decisions

## Summary

- **Feature**: `scan-golden-baseline`
- **Discovery Scope**: Extension（既存コードベースへの統合を伴う新規ツール）
- **Key Findings**:
  - フィクスチャ生成は `\\?\` プレフィクスを付けた通常の `System.IO` API で行える。**app.manifest もレジストリ設定も不要**で、net48 と .NET 10 の両方で同一に動作する（実機検証済み）
  - **アプリ本体に長いパス対応を入れてはならない。** `longPathAware` マニフェストやレジストリ変更を加えると、期待値として記録したい不具合そのものが消える。フィクスチャ生成は被テストアプリと分離した別プロセスで行う必要がある
  - 読み取り権限のないフォルダは**管理者権限なしで作成できる**が、後始末には「Deny ACE の除去 → 削除」の2段階が必須。1段階では削除が失敗する
  - `MAX_PATH` の数え方は**バイト数ではなく文字数（UTF-16 の WCHAR）**。日本語720バイト（247文字）と ASCII 247バイト（247文字）が同じ境界で失敗する。またディレクトリの上限は 248 文字（`MAX_PATH - 12`）でファイルの 260 文字とは異なる

## Research Log

### 長いパスのフィクスチャをどう生成するか

- **Context**: 260文字超のパスを含むフィクスチャが必要だが、被テストアプリが抱えるバグそのものが「.NET Framework の `MAX_PATH` 制限」である。**フィクスチャを作る側も同じ制限に阻まれる**という循環に陥る懸念があった。設計の成否を左右するため、推測ではなく実機検証を行った
- **Sources Consulted**: 下記 References に加え、本開発機（Windows 11 build 26200、`LongPathsEnabled=1`、非昇格ユーザー）での .NET Framework 4.8 / .NET 10.0.8 / PowerShell 5.1 による実測
- **Findings**:
  - ゲートは3層ある。①レジストリ `LongPathsEnabled`、②exe のマニフェスト `longPathAware`、③.NET 独自の `MAX_PATH` チェック。**`\\?\` プレフィクスは ① と ② の両方を迂回する**
  - TFM 4.6.2 以上では ③ が既定で無効のため、`\\?\` を付ければ `Directory.CreateDirectory` / `File.WriteAllBytes` が**そのまま通る**。Win32 の直接呼び出しは不要
  - `\\?\` を付けないプレーンなパスは、.NET のチェックが外れていても Win32 が拒否する（`PathTooLongException` ではなく `DirectoryNotFoundException` が返るのが証拠）
  - .NET 10 ではマニフェストなしでプレーンパスも通る。ランタイムが内部で `\\?\` を自動付与しているため。`\\?\` を明示しても同様に動作する
  - **カレントディレクトリを段階移動して相対パスで作る方式は無効。** `SetCurrentDirectory` 自体が `MAX_PATH` 制限を受け、net48 でも .NET 10 でも失敗する。Win32 の公式文書も「`\\?\` は相対パスに使えない」と明記している
  - `subst` による基準パス短縮は動作するが、割り当て先が260文字未満である必要があり、割り当て後の相対長にも `MAX_PATH` が効くため上限が約500文字に留まる。加えてログオンセッション限定で消えるため CI に不向き
  - PowerShell 5.1 は生成可能だがレジストリ設定に依存する。PowerShell 7 は `longPathAware` の欠落と `\\?\` サポートの退行が報告されており、生成スクリプトのランタイムとして不適
- **Implications**: フィクスチャ生成は `\\?\` 付与を一箇所に閉じ込めた薄いヘルパ経由で行う。この方式のみが「移行をまたいで同一コードが使える」という要件 7.1 を満たす

### 権限のないフォルダの生成と後始末

- **Context**: 走査がアクセス拒否に遭遇しても継続することを検証したいが、権限を落としたフォルダを作れるか、また後始末できるかが不明だった
- **Findings**:
  - 非昇格の一般ユーザーで、自身の SID に対する Deny ACE を付与すれば作成できる。実測で列挙・読み取りとも `UnauthorizedAccessException` になることを確認
  - **Deny を付けたままでは再帰削除が失敗する。** オブジェクトの所有者は DACL に関わらず `READ_CONTROL` と `WRITE_DAC` を暗黙に持つため、「ACE 除去 → 削除」の2段階なら確実に後始末できる
  - GitHub Actions の Windows ランナーは管理者かつ UAC 無効で動作するため ACL 変更は可能。ただし Deny ACE がランナー上で期待どおり拒否として成立するかは**未確認**
  - .NET Framework では `DirectorySecurity` が `System.IO` に標準搭載だが、**.NET 10 では `FileSystemAclExtensions` 経由となり API が変わる**
- **Implications**: ACL 操作を専用の小さな部品に隔離し、移行時の修正がそこだけで済むようにする。teardown の2段階は設計上の必須事項として明記する

### 日本語を含む長いパスの扱い

- **Context**: 日本語パスを扱うツールであり、マルチバイト文字での境界挙動を確定させる必要があった
- **Findings**:
  - 数え方は**文字数（WCHAR）**で確定。日本語247文字（731バイト）と ASCII 247文字（247バイト）が同じ 248 文字で失敗する
  - ディレクトリの境界が 248 文字なのは、8.3 名を追記できる必要があるという Win32 の仕様（`MAX_PATH - 12`）による。ファイルパスは 260 文字
  - Unicode を含む長いパスで .NET が失敗するという未解決の報告があるが、**本機の .NET 10.0.8 では再現しなかった**（日本語509文字で作成・書込・読込がすべて成功）
- **Implications**: フィクスチャの長さ設計では文字数を基準にする。ディレクトリ248・ファイル260という異なる境界を、両方またぐ構造にする

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| アプリ本体に隠しCLIモードを追加 | 既存 exe に `--golden-dump` 相当の引数を足す | 追加プロジェクト不要 | **配布物である本体を検証都合で変更する。** 引数解析の追加が WPF の起動経路に影響しうる | 不採用 |
| 別プロジェクトのコンソールツール（採用） | 本体プロジェクトを参照する独立した exe | 本体を汚さない。フィクスチャ生成と走査を同一プロセスで完結できる | 本体の内部構造変更時にツール側の追従が必要 | 採用 |
| セッションファイルの変換 | 本体が保存する走査結果を読んで期待値に変換 | 本体を一切変更しない | 走査の実行が手作業になり再現性が落ちる。保存形式に依存し要件 7.2 と衝突する | 不採用 |

## Design Decisions

### Decision: フィクスチャ生成を `\\?\` プレフィクス方式に限定する

- **Context**: 260文字超のフィクスチャを、被テストアプリのバグを消さずに生成する必要がある
- **Alternatives Considered**:
  1. アプリまたはツールに `longPathAware` マニフェストを付与する
  2. カレントディレクトリを段階移動して相対パスで作成する
  3. `subst` で基準パスを短縮する
  4. Win32 API を直接呼び出す
- **Selected Approach**: `Path.GetFullPath` で正規化したうえで `\\?\`（UNC の場合は `\\?\UNC\`）を付与し、通常の `System.IO` API を呼ぶ。付与処理は単一のヘルパに閉じ込める
- **Rationale**: 実機検証により、この方式のみが「レジストリ・マニフェスト・グループポリシーのいずれにも依存せず、net48 と .NET 10 の双方で同一に動作する」ことを確認した。案1は**再現したいバグを消してしまう**ため根本的に不適。案2は `SetCurrentDirectory` 自体が制限を受けるため実測で無効と判明。案3は上限約500文字かつセッション依存。案4は動作するが `\\?\` で足りるため不要
- **Trade-offs**: `\\?\` は正規化されないため、付与前に必ず `Path.GetFullPath` を通す規律が必要。`.` / `..` / `/` が使えない
- **Follow-up**: 移行後に .NET 10 上で同一コードが動作することを確認する

### Decision: 期待値の真値をフィクスチャ定義から導く

- **Context**: 要件 5.2 は「既知の不具合に由来するエントリまたは欠落を識別できる情報とともに記録する」ことを求める。これを手作業の注釈で維持すると必ず腐る
- **Alternatives Considered**:
  1. 期待値ファイルに既知不具合の注釈を手で書き込む
  2. フィクスチャの宣言的定義を真値とし、観測結果との差を機械的に導出する
- **Selected Approach**: フィクスチャを宣言的に定義し、その定義が「何が存在するはずか」の真値を持つ。走査で観測された結果との差分を既知の欠落として自動的に識別する
- **Rationale**: 手作業の注釈は更新漏れを起こす。定義から導けば、フィクスチャを変更しても注釈が自動的に追随する。`scan-correctness` で不具合が修正された際、欠落が解消したことも同じ仕組みで確認できる
- **Trade-offs**: フィクスチャ定義が実際の生成結果と一致していることが前提になる。生成失敗時はその旨を記録する必要がある（要件 3.6、3.7）
- **Follow-up**: 生成できなかった項目がある状態の期待値を、完全な期待値と取り違えない仕組みを実装時に確認する

### Decision: 走査条件を固定し表示条件を一切適用しない

- **Context**: 既存の `ResultFormatter` は表示単位への変換で精度を落とし、閾値・折りたたみ・ローカライズを反映する。期待値には使えない
- **Selected Approach**: 走査は「抽出サイズの閾値ゼロ、ファイル表示あり、フィルタなし」の固定条件で実行し、`FolderInfo` ツリーからバイト値を直接射影する
- **Rationale**: 要件 2.2 と 1.2 を満たす唯一の方法。整形経路を通さないことで、表示層の変更が期待値に影響しなくなる
- **Trade-offs**: 表示層の退行は本フィーチャーでは検出できない（要件の対象外であり `ui-redesign` の範囲）

## Risks & Mitigations

- **本体の内部構造変更でツールが追従不能になる** — ツールが本体に触れる面を `Scan` 層の2ファイルに限定し、影響範囲を局所化する。`scan-performance` が `FolderInfo` を変更する際はここだけを直せばよい
- **SDK 形式プロジェクトの glob が新規ツールのソースを本体に取り込む** — 本体 csproj の `DefaultItemExcludes` にツールのディレクトリを追加する。既に `TestPerf\**` を除外している前例がある
- **CI の Windows ランナーで Deny ACE が期待どおり拒否にならない可能性（未確認）** — 生成できなかった項目を報告して継続する仕組み（要件 3.6）で吸収し、期待値には不完全である旨を記録する（要件 3.7）
- **フィクスチャの後始末失敗によるゴミの残留** — teardown を「ACE 除去 → 削除」の2段階とし、削除も `\\?\` 経由で行う
- **移行時に ACL 操作の API が変わる** — ACL 操作を単一の部品に隔離済み。移行時の修正箇所が特定できる

## References

- [Maximum Path Length Limitation - Win32 apps](https://learn.microsoft.com/en-us/windows/win32/fileio/maximum-file-path-limitation) — `MAX_PATH` の定義が文字数であること、ディレクトリが `MAX_PATH - 12` であること、相対パスに `\\?\` が使えないこと
- [File path formats on Windows systems - .NET](https://learn.microsoft.com/en-us/dotnet/standard/io/file-path-formats) — `\\?\` のサポート範囲（.NET Framework 4.6.2 以降、.NET Core 全般）
- [.NET 4.6.2 and long paths on Windows 10](https://learn.microsoft.com/en-us/archive/blogs/jeremykuhne/net-4-6-2-and-long-paths-on-windows-10) — 4.6.2 での長いパス対応の経緯
- [Mitigation: Path Normalization - .NET Framework](https://learn.microsoft.com/en-us/dotnet/framework/migration-guide/mitigation-path-normalization) — `AppContextSwitchOverrides` の指定方法
- [dotnet/runtime#126535](https://github.com/dotnet/runtime/issues/126535) — Unicode を含む長いパスの未解決報告
- [PowerShell/PowerShell#13168](https://github.com/PowerShell/PowerShell/issues/13168) — PowerShell 7 での `\\?\` サポート退行
- [DACLs and ACEs - Win32 apps](https://learn.microsoft.com/en-us/windows/win32/secauthz/dacls-and-aces) — Deny ACE の評価順序
- [GitHub-hosted runners reference](https://docs.github.com/en/actions/reference/runners/github-hosted-runners) — Windows ランナーが管理者かつ UAC 無効であること
