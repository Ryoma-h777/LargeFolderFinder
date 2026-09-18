# Research & Design Decisions

## Summary
- **Feature**: `dotnet10-migration`
- **Discovery Scope**: Complex Integration（実行基盤の移行、配布方式の変更、CI とテスト基盤の新設が同時に起きる）
- **Key Findings**:
  - 単一ファイル化の既知の障害（`ConfigurationManager`、`Properties.Settings`、`app.config`、`Assembly.Location`）を現行コードは踏んでいない。書き込み先はすべて利用者ごとのアプリデータ配下で、実行ファイルの隣には書かない
  - 検証ツール（`Tools/GoldenBaseline`）はアプリを直接参照しており、アプリを .NET 10 にすると net48 のままでは参照できない。ツール内にも .NET 10 に存在しない API（`Directory.GetAccessControl` / `SetAccessControl`）がある
  - 移行だけで、期待値データの「既知の欠落4件」が結果に現れる見込み。走査の本処理は `DirectoryInfo.EnumerateFiles` / `EnumerateDirectories` で、.NET (Core) 以降は長いパスを扱えるため
  - SDK の版の選定が最大のリスク。WPF と単一ファイル発行の組み合わせの不具合は 10.0.2xx 系で報告され未解決。3xx / 4xx 系での状況は公開情報になく、実測で決めるほかない

## Research Log

### 現状のコードと設定（2026-09-17 調査）
- **Context**: 移行の影響範囲を要件に反映するため
- **Findings**:
  - csproj: `WinExe`、`net48`、`UseWPF=true`、`UseWindowsForms=true`、`Nullable=enable`、`LangVersion=12.0`、`Version=1.0.3`、`DefaultItemExcludes` で `Tools\**` と `TestPerf\**` を除外。旧式の `Reference` が11件（.NET 10 では不要）。`FodyWeavers.xml` は `<Costura DisableCompression="false" />` のみ。`app.manifest` とアイコンの指定はない
  - パッケージ: MessagePack 3.1.7、Ookii.Dialogs.Wpf 5.0.1、YamlDotNet 16.3.0、Fody 6.8.2、Costura.Fody 6.0.0
  - `System.Windows.Forms` と `System.Drawing` の使用は0件。`UseWindowsForms=true` は実質不要（外すとき `MessageBox` の型解決に注意）
  - 実行ファイルの場所は8箇所ですべて読み取り（`Config.txt`、`Resources/Languages`、`Resources/Readme`、`Resources/License`）。書き込みは利用者ごとのローカルのアプリデータ配下（ログ、`Settings.msgpack`、`Sessions/`）。アプリデータのフォルダ名は `AssemblyTitle` から取る
  - P/Invoke は `FindFirstFileEx` / `FindNextFile` / `FindClose`（フォルダ数の事前カウント）、`GetDiskFreeSpace`（クラスタサイズ）。`ShowWindow` と `GetCompressedFileSize` は未使用。`SetProcessWorkingSetSize` は使用中
  - 管理者としての再起動は `Process.GetCurrentProcess().MainModule?.FileName` を使う。単一ファイル版では exe 自身を指すため動く見込みだが、フレームワーク依存版を `dotnet app.dll` の形で起動した場合はホストを指す。`Environment.ProcessPath` への置き換えが候補
  - バージョン表示は `AssemblyInformationalVersionAttribute`。.NET 8 以降の SDK は既定でここにコミットハッシュを付けるため、`IncludeSourceRevisionInInformationalVersion=false` が必要
  - `.github/`、`global.json`、`Directory.Build.props`、`nuget.config` はいずれもない
  - 利用者向けの案内（README、`Resources/Readme/`）の配置の記述は現行のコードとずれており、移行にあわせて直す対象になる
- **Implications**: 要件1.4、1.6、2.3〜2.6、3.1〜3.3、8.2 に反映済み

### 外部の最新状態（2026-09-17 調査）
- **Sources Consulted**: .NET 公式のダウンロードとサポート方針、dotnet/sdk#51888、dotnet/wpf#11678、dotnet/wpf#3811、NuGet（MessagePack、YamlDotNet、CommunityToolkit.Mvvm、xunit.v3）、Microsoft Learn（OpenFolderDialog、単一ファイル発行、長いパス）、actions/runner-images
- **Findings**:
  - .NET 10 の最新は 10.0.12（2026-09-08）。SDK は 10.0.401 と 10.0.112。LTS、サポート終了 2028-11-14
  - `--self-contained false` が効かない不具合（dotnet/sdk#51888）は未解決。`--no-self-contained` または `<SelfContained>false</SelfContained>` で回避
  - WPF と単一ファイル発行の不具合（dotnet/wpf#11678）は未解決。10.0.200 以降で起動時の例外、10.0.202 以降でフォルダ名による無言終了とビルドエラーの報告。10.0.103 と 10.0.106 では出ないとされる。3xx / 4xx 系の情報はない
  - WPF のトリミング非対応（dotnet/wpf#3811）は Future のまま。自己完結版の大きさは削減できない
  - MessagePack: 最新 3.1.8（2026-07-08）。脆弱性の修正版は 3.1.7。3.1.8 が安全側
  - YamlDotNet: 最新 18.1.0。16.3.0 以降に破壊的変更が複数（17.0.0 の再帰深度、17.1.0 の `MergingParser` の上限、18.0.0 の `ITypeInspector` への追加、18.1.0 の既定の再帰レベル130）。本アプリは独自の型検査を持たず、平坦な辞書しか読まないため影響は小さい見込み
  - CommunityToolkit.Mvvm: 8.4.2 が最新。8.4.0 は .NET 10 でビルド不能（修正済みの課題として記録あり）
  - `Microsoft.Win32.OpenFolderDialog` は .NET 8 以降の WPF に同梱。`Title`、`InitialDirectory`、`FolderName`、`Multiselect` などを持つ。現行のダイアログの使い方（説明文とタイトル、開始フォルダ）は代替できる
  - xunit.v3 は 4.0.1（2026-09-12）。4.0.0 で Microsoft Testing Platform v2 が既定。.NET 10 SDK では `global.json` の `test.runner` でモードを選ぶ
  - GitHub Actions の `windows-2025` には SDK 10.0.111 / 10.0.204 / 10.0.303 / 10.0.400 がプリインストール。`actions/setup-dotnet` の最新メジャーは v6。GUI アプリの起動確認はホスト型ランナーでも可能とみられるが、公式の保証はなく最初に試す必要がある
  - .NET (Core) 以降の `System.IO` は長いパスを設定なしで扱う。Win32 API を直接呼ぶ経路は、レジストリとマニフェストの両方が必要
- **Implications**: 要件3.4〜3.6、5.2、5.6、5.7、前提の記述に反映済み

## Requirement-to-Asset Map

| Requirement | 既存の資産 | 状態 |
|-------------|-----------|------|
| 1.1〜1.5 実行基盤の移行 | `LargeFolderFinder.csproj`、アプリ全体 | Constraint（旧式の `Reference` 11件、`UseWindowsForms` の要否をビルドで確認） |
| 1.6 不要なものの除去 | 未使用の P/Invoke 2件、未使用の定数 `AppIconFileName` | 既存（移行に必要な範囲で判断） |
| 2.1, 2.2 2形態の配布 | Costura による単一 exe | Missing（発行の設定を新設） |
| 2.3, 2.4 隣のファイルの解決 | 実行ファイルの場所を使う8箇所 | Constraint（単一ファイル版で発行フォルダに出るかを実測） |
| 2.5 管理者として再起動 | `MainModule.FileName` を使う実装 | Constraint（フレームワーク依存版での挙動を確認） |
| 2.6 バージョン表示 | `AssemblyInformationalVersion` | Constraint（コミットハッシュの付加を止める設定が必要） |
| 2.7, 2.8 配布物の構成と案内 | README、`Resources/Readme/` | Constraint（現行の記述がコードとずれている） |
| 3.1 埋め込みの置き換え | `FodyWeavers.xml`、Fody 2件 | 既存（削除対象） |
| 3.2, 3.3 フォルダ選択 | `BrowseButton_Click` の1箇所 | 既存（置き換え） |
| 3.4〜3.6 依存の更新 | パッケージ参照 | 既存（版の引き上げ） |
| 3.7 著作権表示 | `Resources/License/ThirdPartyNotices.txt`（5件を記載） | 既存（増減の反映） |
| 4.1〜4.3 走査結果の同等性 | `baselines/fixture-v1.golden.txt`（17件、既知の欠落4件）、比較ツール | 既存（期待値の更新が必要） |
| 4.4 保存データの読み書き | MessagePack による設定・セッション | 既存（動作確認） |
| 5.1〜5.8 自動ビルド | なし | Missing（CI を新設） |
| 6.1〜6.5 テストの基盤 | なし。判定の仕組みは検証ツールにある | Missing（テストプロジェクトを新設し、既存の判定を呼ぶ） |
| 7.1, 7.2 走査結果の検証ツール | `Tools/GoldenBaseline`（net48、アプリを参照、ACL API を使用） | Constraint（対象フレームワークの変更と API の置き換え） |
| 7.3 翻訳の網羅の検証ツール | `Tools/LocalizationCheck`（net48、アプリを参照。2026-09-18 に `localization-completeness` で完成。`check` と `selfcheck`、終了コード 0/1/2） | Constraint（対象フレームワークの変更。アプリの exe から `LanguageKey` を得る。YamlDotNet を上げたら、重複キーと空のファイルの扱いを `selfcheck` で取り直す） |
| 8.1〜8.3 記録の更新 | `.kiro/steering/tech.md`、`product.md`、README | 既存（更新） |

## 実装方針の選択肢

### Option A: 一括で移行する
アプリと検証ツールを同時に .NET 10 へ移し、依存の整理・CI・テストまでを1つの流れで進める。
- ✅ 中間状態（アプリだけ .NET 10、ツールが参照できない）が生じない
- ❌ 失敗したときの切り分けが難しい。過去に「1回目の移行で仕様が壊れた」記録がある

### Option B: 段階を分ける
(1) 移行の前に済ませられる整理（Ookii の置き換え、未使用コードの除去、`global.json` の追加）→ (2) 対象フレームワークの変更とツールの追従 → (3) 配布方式の変更 → (4) CI とテスト基盤。
- ✅ 各段階で既存の期待値データと比較でき、原因の切り分けがしやすい
- ✅ 過去の失敗（一度に変えて壊れた）を繰り返しにくい
- ❌ 段階ごとにビルドと確認の手間がかかる

### Option C: 検証の土台を先に作る
テストプロジェクトと CI を先に net48 のまま作り、その上で移行する。
- ✅ 移行の影響をテストで測れる
- ❌ テスト基盤を net48 と .NET 10 の2回作ることになりかねない（xunit.v3 は net472 以上に対応するため不可能ではない）

**推奨**: Option B。段階の境目を、既存の期待値データとの比較が成立する位置に置く。

## Implementation Complexity & Risk

| 領域 | Effort | Risk | 理由 |
|------|--------|------|------|
| 対象フレームワークの変更とコード修正 | M | Medium | 固有 API の使用が少なく、置き換えの対象は明確。ただしビルドの警告と WPF の挙動差は実測が要る |
| 配布方式の変更（単一ファイル発行） | M | High | SDK の版に依存する未解決の不具合があり、実測で版を決める必要がある |
| 依存の整理 | S | Low | 置き換え先が明確。YamlDotNet の版上げだけ挙動確認が必要 |
| 検証ツールの追従 | M | Medium | ACL の API の置き換えが必要。影響はツール内の1ファイルに閉じている |
| CI の新設 | M | Medium | GUI アプリの起動確認がホスト型ランナーで成立するかを最初に試す必要がある |
| テスト基盤の新設 | M | Medium | xunit.v3 4.x は実行の仕組みが変わっており、資料の版に注意が要る |
| 走査結果の差の確認と期待値の更新 | S | Low | 比較ツールと既知の欠落の判定が既にある |

## Research Needed（設計フェーズで確かめる）
- SDK の候補（10.0.112 と 10.0.401）で、WPF の単一ファイル版が起動するか。起動しない場合は 10.0.106 系に落とす
- 単一ファイル発行で、`Config.txt` と `Resources/` 配下が発行フォルダに出るか（`ExcludeFromSingleFile` の要否）
- フレームワーク依存版での「管理者として再起動」の挙動（`MainModule.FileName` と `Environment.ProcessPath` の違い）
- ホスト型ランナーで WPF の exe を起動して生存を確かめられるか。できない場合の代替（起動時に自己診断して終了コードを返す起動引数など）
- 期待値データの差が、既知の欠落4件と親フォルダのサイズだけに収まるか（フィクスチャの生成が .NET 10 でも同じ構造を作れるか）
- テストから検証ツールの判定を呼ぶ形（プロジェクト参照か、判定部分の共有か）
- ~~`localization-completeness` の検証ツールの実装順~~ → 2026-09-18 に完成済み。本スペックでは移行の対象になる

---
