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

## 設計フェーズの実測（2026-09-18、リポジトリの複製で実施）

複製（本物のリポジトリは変更していない）を .NET 10 に移し、発行・起動・比較を実測した。起動試験ではアプリのデータフォルダ名を変え、利用者の設定に触れないようにした。

### 最小の移行とビルド
- 対象を `net10.0-windows` にし、旧式の `Reference` 11件、Fody・Costura.Fody・`FodyWeavers.xml`、`UseWindowsForms` を外すと、エラーは1件だけ: `Views/MainWindow.xaml.cs` の `File.GetAccessControl(path)`（.NET 10 に無い）。`new FileInfo(path).GetAccessControl()` で解消する。ギャップ分析の一覧に無かった箇所
- 警告は移行前 0件 → 移行後 5件（WPF の二重コンパイルで表示は10件）。`Helpers/RelayCommand.cs` の null 許容の注釈（CS8767 ×2、CS8612 ×1）と、所有者の取得の null の扱い（CS8602 ×2）。要件1.1 を満たすにはこの5件を直す
- `UseWindowsForms` を外しても `MessageBox` を含めて問題なし。Ookii.Dialogs.Wpf は残してもビルドできるが、`OpenFolderDialog`（`Title`、`InitialDirectory`、`FolderName`、`ShowDialog(owner)`）への置き換えもビルドできる

### SDK の版ごとの発行と起動
| SDK | 自己完結の単一 exe | `--no-self-contained` | `--self-contained false` |
|---|---|---|---|
| 10.0.108 / 10.0.112（1xx） | 起動可（約140MB） | 起動可（約1MB） | 起動可だが**自己完結になる**（約132MB）＝ sdk#51888 を再現 |
| 10.0.303 / 10.0.401 | 起動可（約140MB） | 起動可（約1MB） | 起動可（約1MB） |

- wpf#11678（フォルダ名による無言終了）は、空白・日本語・括弧を含むフォルダでも再現しなかった（10.0.112、10.0.303、10.0.401）。2xx 系は手元に無く未確認
- 自己完結版に同梱されるランタイムは SDK の版で決まる。2026-09-08 のセキュリティ修正（ランタイム 10.0.12）を含むのは SDK 10.0.401 と 10.0.112 だけ

### 発行物
- `Config.txt` と `Resources/` 配下（言語13、Readme 13、ライセンス2）は、既存の `Content` の設定のまま exe の隣に出る。追加の指定は不要。アプリはそこから言語ファイルを読めた
- `LargeFolderFinder.pdb` も発行フォルダに出る
- 大きさ（10.0.401）: 自己完結 140.8MB（zip 118.2MB）、フレームワーク依存 1.28MB（zip 1.08MB）。`EnableCompressionInSingleFile=true` は展開後 65.4MB になるが **zip は 119.2MB で縮まず**、ウィンドウが出るまでが 0.2〜0.6 秒遅くなる

### バージョン表示
- 発行した exe も、**現行の net48 ビルドも**、`ProductVersion` は `1.0.3+<コミットハッシュ>`。今の SDK の既定の動き。`IncludeSourceRevisionInInformationalVersion=false` で `1.0.3` だけになる

### 実行ファイルのパス
- `MainModule.FileName` と `Environment.ProcessPath` は、自己完結・フレームワーク依存の単一 exe のどちらでも exe 自身を返す。`dotnet X.dll` の形で起動したときは両方とも dotnet.exe を返すので、置き換えても差はない。`AppContext.BaseDirectory` は常に exe のフォルダ

### 走査結果の同等性（GoldenBaseline を net10 にして実測）
- `AccessControlGate.cs` を `DirectoryInfo` の拡張メソッドに置き換えるとビルドできる
- `compare`: 終了コード 1、差分12件。**既知の欠落4件が `[Unexpected]` として現れ、それを含む親フォルダ8件のサイズが 0→8 に変わった**。走査の報告の「説明できない欠落」は0件、アクセス拒否のフォルダのスキップは移行前と同じ。要件4.1 の想定どおり
- `selfcheck`: **128件中13件が失敗**。13件すべて「net48 では長いパスを作れない・列挙できない」ことを前提にした項目（例: 260文字超のフォルダ作成が成功してしまう、既知の欠落が4件出るはずが0件）

### 翻訳の網羅の検証ツール、依存
- `Tools/LocalizationCheck` は対象を変えるだけで、`selfcheck` 59件成功・`check` 問題0件。YamlDotNet 18.1.0 でも同じ結果。アプリの `Config.txt` も 16.3.0 と 18.1.0 の両方で正しく読めた
- MessagePack 3.1.8 と 3.1.9（2026-09-17 公開）でビルド・発行でき、脆弱性の登録なし。net48 が書いた設定とセッションを net10 が読め、逆も読めた（読み直して保存した結果はバイト単位で一致）
- net10 では推移的依存が MessagePack.Annotations などに減り、net48 で入っていた `System.Memory` などは不要になる

## 設計判断（実測を受けて）

### Decision: SDK は 10.0.4xx 系に固定する
- **Context**: 要件5.6、5.7。自己完結版は SDK に付いているランタイムを同梱して配る
- **Alternatives**: (1) 10.0.303（手元と CI に既にある） (2) 10.0.401（最新、ランタイム 10.0.12） (3) 10.0.112（1xx、`--self-contained false` の不具合あり）
- **Selected**: `global.json` で `10.0.401`、`rollForward: latestPatch`（4xx 系のパッチだけ追う）
- **Rationale**: 自己完結版に最新のセキュリティ修正を含むランタイムを入れるため。4xx 系は実測で起動・発行とも問題がなかった
- **Trade-offs**: 開発者の PC に SDK 10.0.4xx の導入が要る（手元には 10.0.303 までしかない）

### Decision: 単一ファイルの圧縮は使わない
- zip の大きさが縮まず、起動が遅くなるだけのため

### Decision: テストは検証ツールの実行ファイルを呼び、終了コードで判定する
- **Context**: 要件6.2、6.5（既存の判定を作り直さない）
- **Alternatives**: (1) テストが検証ツールの部品（`FixtureBuilder`、`ScanRunner`、`BaselineComparer` など）を組み立て直す (2) テストが検証ツールの `compare`・`selfcheck`・`check` を子プロセスで実行し、終了コードと出力で判定する
- **Selected**: (2)
- **Rationale**: 判定の組み立てを重複させず、開発者が手で打つコマンドと CI とテストが同じ入口を通る

### Decision: GoldenBaseline の自己検証のうち、net48 の制約を前提にした13件を書き直す
- 要件7.5 が守るのは「期待値の形式」と「判定の規則」で、実行基盤の制約を確かめる自己検証項目はその外にある。移行後の事実（長いパスを作れる・列挙できる）に合わせて期待を改め、改めた項目と理由を記録する

### Decision: バージョン文字列からコミットハッシュを外す
- 要件2.6 の「余分な付加情報のない形」に合わせ、`IncludeSourceRevisionInInformationalVersion=false` とする。現行のビルドにも付いていたことは記録に残す

## 移行の記録

本スペックの実装で「記録する」ものはこの節に書く。

### 1.1 SDK の固定と移行前の控え（2026-09-19、コミット 48d8a5d の上で実施）
- **SDK の固定**: リポジトリ直下に `global.json` を追加した（SDK `10.0.401`、`rollForward: latestPatch`、テストの実行方式 `Microsoft.Testing.Platform`）。手元の SDK は 6.0.423、8.0.416、9.0.318、10.0.108、10.0.303、10.0.401
  - 追加前: `dotnet --info` の「global.json file」は `Not found`。SDK 10.0.401 の導入後だったため、固定が無くても最新の `10.0.401` が選ばれていた（導入前は `10.0.303`）
  - 追加後: `dotnet --version` は `10.0.401`、`dotnet --info` の「global.json file」はリポジトリ直下の `global.json` を指す
- **固定した SDK での確認（対象は net48 のまま、ビルドは順番に `--no-incremental` で実施）**

| コマンド | 結果 | 警告 | 終了コード |
|---|---|---|---|
| `dotnet build LargeFolderFinder.csproj -c Debug` | 成功 | 0 | 0 |
| `dotnet build LargeFolderFinder.csproj -c Release` | 成功 | 0 | 0 |
| `dotnet build Tools/GoldenBaseline/GoldenBaseline.csproj` | 成功 | 0 | 0 |
| `dotnet build Tools/LocalizationCheck/LocalizationCheck.csproj -c Debug -p:BuildProjectReferences=false` | 成功 | 0 | 0 |
| `GoldenBaseline.exe selfcheck` | 128 件中 0 件が失敗 | — | 0 |
| `GoldenBaseline.exe compare --golden baselines/fixture-v1.golden.txt` | 判定: 一致（既知の欠落 4 件、説明できない欠落 0 件、スキップ 1 件） | — | 0 |
| `LocalizationCheck.exe check` | 問題はありません（言語 13、キー 81） | — | 0 |
| `LocalizationCheck.exe selfcheck` | 59 件中 0 件が失敗 | — | 0 |

  - 移行前の警告数の基準は、アプリ・2つの検証ツールとも **0 件**
  - 検証ツールの実行後、`%TEMP%` に `gb_fix_*` の一時フォルダは残っていない
- **バージョン管理から外したもの**: `.gitignore` に `artifacts/` を加えた
- **移行前の版の控え**: `artifacts/legacy-net48/` に、上の手順でビルドした **Release 構成**の出力（`bin/Release/net48/`）一式をそのまま複製した（`LargeFolderFinder.exe`、`.exe.config`、`.pdb`、`Config.txt`、`Languages/`、`Readme/`、`License/`、`Resources/`）。exe の `FileVersion` は `1.0.3.0`、`ProductVersion` は `1.0.3+48d8a5dfddf57a4e87216a730bd78560073d8b40`。要件4.4 の確認（5.4）で使う

### 2.1 アプリのプロジェクト設定の移行（2026-09-19、コミット fbfc66a の上で実施）
- **対象の変更**: `LargeFolderFinder.csproj` の `TargetFramework` を `net48` から `net10.0-windows` に変えた。`UseWPF=true` は維持
- **加えたもの**: `RuntimeIdentifier=win-x64`、`IncludeSourceRevisionInInformationalVersion=false`。`DefaultItemExcludes` に `Tests\**`、`build\**`、`artifacts\**` を加えた（既存の `TestPerf\**`、`Tools\**` はそのまま）
- **依存の版**: MessagePack 3.1.7 → 3.1.9、YamlDotNet 16.3.0 → 18.1.0（`obj/project.assets.json` で解決された版を確認）
- **取り除いたもの**（要件1.6、3.1）
  - 不要な UI 基盤の指定: `UseWindowsForms=true`（`System.Windows.Forms` の使用は0件。外しても `MessageBox` を含めてビルドできた）
  - 旧式の `Reference` 11件: `System`、`System.Data`、`System.Xml`、`Microsoft.CSharp`、`System.Core`、`System.Xaml`、`WindowsBase`、`PresentationCore`、`PresentationFramework`、`System.Net.Http`、`System.Configuration`
  - 依存 DLL の埋め込みの仕組み: パッケージ `Fody` 6.8.2 と `Costura.Fody` 6.0.0、設定ファイル `FodyWeavers.xml`（`<Costura DisableCompression="false" />` のみ）と `FodyWeavers.xsd`。単一ファイルの発行は 3.1 のスクリプトで行う
- **API の置き換え**（要件1.4）: `Views/MainWindow.xaml.cs` の所有者の取得で、.NET 10 に無い `File.GetAccessControl(path)` を `new FileInfo(path).GetAccessControl()`（`System.IO.FileSystemAclExtensions`）に置き換えた。フォルダ側は既に `new DirectoryInfo(path).GetAccessControl()` だったので変えていない。null の扱いの明示は 2.2 で行う
  - 置き換え前のビルド（Debug）: エラー1件 `Views/MainWindow.xaml.cs(779,37): error CS1929: 'File' に 'GetAccessControl' の定義が含まれておらず…`、警告4件
- **この時点で残したもの**
  - `Ookii.Dialogs.Wpf` 5.0.1（2.3 で標準のダイアログに置き換える。.NET 10 でもビルドでき、出力に `Ookii.Dialogs.Wpf.dll` が出る）
  - 見つけたが移行に不要なため残したもの（設計の Non-Goals）: 未使用の P/Invoke 2件 `Helpers/Win32.cs` の `ShowWindow`（59行）と `GetCompressedFileSize`（78行）、未使用の定数 `Models/AppConstants.cs` の `AppIconFileName`（39行）
- **変えていないもの**: `Version`、`Title`、`Copyright`、`AssemblyName`、`RootNamespace`、`Nullable`、`LangVersion`、`Content` の設定。アセンブリのタイトル（アプリデータのフォルダ名の元、要件1.3）は移行前の exe と同じ `LargeFolderFinder`（`FileDescription` で確認）
- **アプリのプロジェクト単体のビルド**（`dotnet build LargeFolderFinder.csproj -c <構成> --no-incremental`、順番に実施）

| 構成 | 結果 | エラー | 警告（表示） | 警告（重複を除く） | 終了コード |
|---|---|---|---|---|---|
| Debug | 成功 | 0 | 10 | 5 | 0 |
| Release | 成功 | 0 | 10 | 5 | 0 |

  - 警告は WPF の二重コンパイル（`*_wpftmp.csproj` と本体）で同じものが2回ずつ表示される。重複を除いた5件は設計の予測と一致した

| 設計の予測 | 実際の警告 |
|---|---|
| `Helpers/RelayCommand.cs` CS8767 | `(17,21)` `CanExecute(object parameter)` と `ICommand.CanExecute(object? parameter)` の null 許容の不一致 |
| `Helpers/RelayCommand.cs` CS8767 | `(22,21)` `Execute(object parameter)` と `ICommand.Execute(object? parameter)` の null 許容の不一致 |
| `Helpers/RelayCommand.cs` CS8612 | `(27,35)` `event EventHandler CanExecuteChanged` と `event EventHandler? ICommand.CanExecuteChanged` の不一致 |
| 所有者の取得 CS8602 | `Views/MainWindow.xaml.cs(779,37)` ファイルの所有者（`GetOwner(...)` の戻り値の `ToString()`） |
| 所有者の取得 CS8602 | `Views/MainWindow.xaml.cs(783,37)` フォルダの所有者（同上） |

  - 5件の解消は 2.2 で行う。この時点では抑制していない
- **出力**: 出力先は `RuntimeIdentifier` の指定により `bin/<構成>/net10.0-windows/win-x64/` になった（移行前は `bin/<構成>/net48/`）。`Config.txt` と `Resources/`（言語13、Readme 13、ライセンス2）が隣に出る。Release の `LargeFolderFinder.dll` の `ProductVersion` は `1.0.3`（移行前の exe は `1.0.3+48d8a5d…`）、`FileVersion` は `1.0.3.0`
  - 依存の DLL として `MessagePack.dll`、`MessagePack.Annotations.dll`、`Microsoft.NET.StringTools.dll`（MessagePack の推移的依存）、`YamlDotNet.dll`、`Ookii.Dialogs.Wpf.dll` が出る。著作権表示の見直し（5.1）で扱う
- ソリューション全体のビルドは、検証ツールが net48 のままのため 2.5 まで通らない（実施していない）

### 2.2 移行で増えた警告の解消（2026-09-19、コミット 6f1b8c5 の上で実施）
- **変更前の観測**: `dotnet build LargeFolderFinder.csproj -c Debug --no-incremental` で警告10件（重複を除いて5件）。2.1 の表の5件と同じ
- **コマンドの共通部品**（`Helpers/RelayCommand.cs`、CS8767 ×2、CS8612 ×1）: `CanExecute(object?)`、`Execute(object?)`、`event EventHandler? CanExecuteChanged` に変えた。保持する処理の型も `Action<object?>`、`Predicate<object?>?` に合わせた（コンストラクタの引数の型も同じ）。処理の中身は変えていない。`RelayCommand` の利用箇所はリポジトリ内に0件（XAML を含めて確認）
- **所有者の取得**（`Views/MainWindow.xaml.cs` の `ShowOwner`、CS8602 ×2）: `GetOwner(...)` の戻り値に `?.ToString() ?? "(Unknown)"` を使い、所有者が得られない（null）ときの表示を明示した
  - 従来（net48）の経路: `GetOwner(...)` が null → `.ToString()` で `NullReferenceException` → 内側の `catch` が受けて `owner = "(Unknown)"` → 画面の所有者の欄に `(Unknown)`。修正後も同じ文字列が同じ欄に出る。ログは従来も出ていなかった（内側の `catch` はログを書かない）ので、差はない
- **警告の抑制**（`#pragma`、`!`、`NoWarn`）は使っていない
- **アプリのプロジェクト単体のビルド**（`--no-incremental`、順番に実施）

| 構成 | 結果 | エラー | 警告 | 終了コード |
|---|---|---|---|---|
| Debug | 成功 | 0 | 0 | 0 |
| Release | 成功 | 0 | 0 | 0 |

  - 移行前の基準（0件、1.1）と同じになった

### 2.3 フォルダ選択ダイアログの置き換え（2026-09-19、コミット 8fb6ea9 の上で実施）
- **変更箇所**: `Views/MainWindow.xaml.cs` の `BrowseButton_Click` と `LargeFolderFinder.csproj`（`Ookii.Dialogs.Wpf` 5.0.1 の `PackageReference` を削除）。訳文と言語ファイルは変えていない（既存のキー `FolderLabel` をそのまま使う）
- **プロパティの対応**

| 従来（`Ookii.Dialogs.Wpf.VistaFolderBrowserDialog`） | 置き換え後（`Microsoft.Win32.OpenFolderDialog`） |
|---|---|
| `Description = GetText(FolderLabel)` + `UseDescriptionForTitle = true`（説明の文言をタイトルに出す） | `Title = GetText(FolderLabel)` |
| `SelectedPath = pathTextBox.Text`（無条件に設定） | 入力欄の値が空白でなく `Directory.Exists` が true のときだけ `InitialDirectory = 入力欄の値` |
| `ShowDialog()`（所有者の指定なし。Ookii は作動中のウィンドウを所有者に使う） | `ShowDialog(this)`（メインウィンドウを所有者に明示） |
| 結果 `SelectedPath` | 結果 `FolderName` |

- **経路の対比**
  - 選ばれたとき: 従来も置き換え後も、戻り値が true のときだけ入力欄の `Text` とセッションの `Path` に反映し、`OnPropertyChanged(nameof(Sessions))` を呼び、ログを1行書く。ログの文言は `Folder selected via Ookii: <パス>` から `Folder selected via OpenFolderDialog: <パス>` に変えた
  - キャンセルしたとき: 従来も置き換え後も `ShowDialog` が true 以外を返し、何も反映しない・ログも書かない
  - 入力欄が空・存在しないパスのとき: 従来は値をそのまま `SelectedPath` に渡し、開始位置の扱いは部品に任せていた。置き換え後は `InitialDirectory` を指定せず、開始位置は OS の既定（直前に使ったフォルダなど）になる
  - 例外: 従来と同じ `try`/`catch` の中にあり、失敗時のログと `MessageBox` は変えていない
  - 入口のログ `Browse button clicked.`（`AppConstants.LogBrowseButtonClicked`）は変えていない
- **ビルド**（`dotnet build LargeFolderFinder.csproj -c <構成> --no-incremental`、順番に実施）

| 構成 | 結果 | エラー | 警告 | 終了コード |
|---|---|---|---|---|
| Debug | 成功 | 0 | 0 | 0 |
| Release | 成功 | 0 | 0 | 0 |

- **Ookii が消えたことの確認**: ソース・csproj（`bin`/`obj` を除く `*.cs`・`*.csproj`・`*.xaml`）、`obj/project.assets.json`、`bin/<構成>/net10.0-windows/win-x64/` のファイル一覧と `LargeFolderFinder.deps.json` のいずれにも `Ookii` が0件。2.1 の時点で出力にあった `Ookii.Dialogs.Wpf.dll` は、`--no-incremental`（再ビルドが前回の出力を消す）で Debug・Release とも消えた
- **ダイアログの実機での確認**（タイトル、開始位置、選択の反映、キャンセル）は GUI 操作のためこの時点では行っていない。5.4 の手順で利用者が確認する
- `ThirdPartyNotices.txt` の Ookii の節と steering の記述は、それぞれ 5.1・5.2 で扱う（この時点では残っている）

### 2.4 走査結果の検証ツールの移行（2026-09-19、コミット 7932b61 の上で実施）
- **対象の変更**: `Tools/GoldenBaseline/GoldenBaseline.csproj` の `TargetFramework` を `net48` から `net10.0-windows` に変えた。それ以外（`OutputType`、`Nullable`、`LangVersion`、`AssemblyName`、`RootNamespace`、アプリへの `ProjectReference`）は変えていない。ツール自身には `RuntimeIdentifier` を指定していない（設計の指定は対象フレームワークだけ）
  - アプリの参照は `bin/Debug/net10.0-windows/win-x64/LargeFolderFinder.dll` で解決された。ツールの出力先は `Tools/GoldenBaseline/bin/Debug/net10.0-windows/`（移行前は `bin/Debug/net48/`）
  - 対象だけを変えた時点のビルド: エラー4件（`Fixture/AccessControlGate.cs` の `Directory.GetAccessControl` ×2 が CS1929、`Directory.SetAccessControl` ×2 が CS1501）。それ以外のエラー・警告はなかった
- **アクセス権の API の置き換え**（要件7.2、`Fixture/AccessControlGate.cs`）

| 移行前（net48） | 移行後（.NET 10、`System.IO.FileSystemAclExtensions`） |
|---|---|
| `Directory.GetAccessControl(directoryPath)` | `new DirectoryInfo(directoryPath).GetAccessControl()` |
| `Directory.SetAccessControl(directoryPath, security)` | `new DirectoryInfo(directoryPath).SetAccessControl(security)`（取得に使った同じ `DirectoryInfo` で書き戻す） |

  - 拒否の内容は変えていない: 実行ユーザー自身の SID に対する Deny ACE、権利は `ListDirectory | ReadData | ReadAttributes | ReadExtendedAttributes | ExecuteFile`、継承は `ContainerInherit | ObjectInherit`、`PropagationFlags.None`（`CreateDenyRule` は無変更）
  - 順序は変えていない: 付与は「取得 → `AddAccessRule` → 書き戻し」、解除は「取得 → `RemoveAccessRule`（一致するルールが無ければ何もしない）→ 書き戻し」。呼び出し側の後始末の順序（解除 → 残留の除去）にも触れていない
- **自己検証の期待の改訂**（要件7.4、7.5）: 対象だけを変えて置き換えを済ませた状態で `selfcheck` を実行すると **128件中13件が失敗**（終了コード 1）。失敗した13件は設計段階の実測と同じ件数で、すべて「.NET Framework では長いパスを作れない・列挙できない」ことを前提にした項目だった。次の13件の期待を改めた（名前が事実と食い違うものは名前も改めた）

| # | 旧い名前（改めたものは新しい名前も） | 旧い期待 | 新しい期待 |
|---|---|---|---|
| 1 | 変換を通すと260文字を超えるフォルダの作成に成功し、変換を通さないと失敗する（要件3.1）<br>→ 変換を通すと260文字を超えるフォルダの作成に成功し、変換を通さなくても成功する（要件3.1） | プレーンなパスでの260文字超のフォルダ作成は例外で失敗し、フォルダは存在しない | プレーンなパスでも例外なく作成でき、プレーンなパス・拡張長パスのどちらで見ても存在する |
| 2 | FixtureBuilder が FixtureSpec.Standard の全項目を拡張長パス経由で実在確認できる形で生成し、ファイルサイズが定義と一致する（Postconditions、要件3.1〜3.3）（名前は変えていない） | 対比として、相対248文字超の長いフォルダはプレーンなパスでは「存在しない」と判定される | プレーンなパスでも「存在する」と判定される（拡張長パス経由と同じ結論）。全項目の生成とサイズの照合は変えていない |
| 3 | ScanRunner が走査結果に手を加えず、長いパスの項目は現行版のまま走査結果に現れない（要件5.1, 5.5）<br>→ ScanRunner が走査結果に手を加えず、長いパスの項目も走査結果に現れる（要件5.1, 5.5） | 設計上の長さ（フォルダ248・ファイル260）を超える LongPath の項目が走査結果に現れない | それらの項目がすべて走査結果に現れる。浅い階層が現れることの確認は変えていない |
| 4 | GoldenProjector が走査結果に手を加えず、長いパスの項目は射影結果にも現れない（要件5.1）<br>→ GoldenProjector が走査結果に手を加えず、長いパスの項目は射影結果にもそのまま現れる（要件5.1） | 長いパスの項目が射影結果に現れない（補完しない） | 長いパスの項目が走査結果に現れ、射影結果にもそのまま現れる（落とさない） |
| 5 | KnownIssueAnalyzer が実フィクスチャの走査結果に対し、長いパスの項目を境界条件を理由とした既知の欠落として列挙する（要件5.2）<br>→ KnownIssueAnalyzer が実フィクスチャの走査結果に対し、長いパスの項目も観測されるため既知の欠落を列挙しない（要件5.2） | 実フィクスチャの走査に欠落があり（248文字超のフォルダ・260文字超のファイルを含む）、それらが根拠 LongPath の既知の欠落として列挙される | 欠落は0件で、248文字超のフォルダ・260文字超のファイルが観測され、既知の欠落は0件。LongPath の欠落を列挙する規則は、人工的な定義を使う既存の項目（改めていない）が引き続き守る |
| 6 | 日本語を含む長いパス（相対パスがフィクスチャ設計上の長さを超える）の項目が、生成されているのに現行版の走査で観測されず、KnownIssueAnalyzer が根拠 LongPath とともに既知の欠落として列挙する（要件3.1, 3.2, 5.1, 5.2、タスク6.3）<br>→ 日本語を含む長いパス（相対パスがフィクスチャ設計上の長さを超える）の項目が、生成され、走査と射影の両方で観測され、KnownIssueAnalyzer が既知の欠落として列挙しない（要件3.1, 3.2, 5.1, 5.2、タスク6.3） | 日本語を含む長いパスの項目は生成されているが走査・射影に現れず、根拠 LongPath の既知の欠落として列挙される | 生成され、走査・射影の両方に現れ、既知の欠落として列挙されない。定義の前提（非ASCII、長さ、最上位の日本語フォルダが観測されること）は変えていない |
| 7 | generate がプロセスとして完走し、終了コード0を返し、既知の欠落（長いパス由来）が報告に含まれ、期待値ファイルが書き出される（要件2.1, 3.7, 5.2）<br>→ generate がプロセスとして完走し、終了コード0を返し、既知の欠落（長いパス由来）が0件と報告され、期待値ファイルが書き出される（要件2.1, 3.7, 5.2） | 標準出力の「原因: LongPath」がちょうど4件 | 「原因: LongPath」が0件で、FixtureSpec.Standard の LongPath の項目がすべて期待値ファイルに記録される。終了コード0・見出し・書き出し・残留なしは変えていない |
| 8 | generate の標準出力に、FixtureSpec.Standard の LongPath トレイトを持つ項目が過不足なく「相対パス（原因: LongPath）」の形で列挙される（要件5.2、3.7）<br>→ generate の標準出力で、FixtureSpec.Standard の LongPath トレイトを持つ項目が観測されるため「相対パス（原因: LongPath）」の明細が1件も列挙されず、既知の欠落の件数行が0件になる（要件5.2、3.7） | LongPath の各項目の明細行があり、件数行が「既知の欠落（境界条件に由来）: 4 件」、「（原因: LongPath）」の出現回数が4 | LongPath の各項目の明細行が無く、件数行が「既知の欠落（境界条件に由来）: 0 件」、出現回数が0 |
| 9 | ScanRunner が、絶対パスが258・259・260・261文字ちょうどのフォルダを含む基準フォルダでも例外を外へ漏らさず走査を完了し、長さのせいで列挙できない対象をアクセス拒否によるスキップと取り違えない（要件2.4、タスク7.2）<br>→ ScanRunner が、絶対パスが258・259・260・261文字ちょうどのフォルダを含む基準フォルダでも例外を外へ漏らさず走査を完了し、それらの直下を列挙でき、アクセス拒否によるスキップと取り違えない（要件2.4、タスク7.2） | 258〜261文字の4フォルダが「長さのせいで列挙できなかった対象」にちょうど4件記録され、257文字のフォルダは記録されない | 「長さのせいで列挙できなかった対象」は0件で、257〜261文字の各フォルダの直下のファイル（`child.txt`）が走査結果に現れる。例外を漏らさないこと、スキップが読み取り拒否フォルダ1件だけであること、境界のフォルダ・257文字のフォルダがスキップに含まれないことは変えていない |
| 10 | 実効絶対パス長105文字の基準フォルダで generate が完走して終了コード0を返し、既知の欠落（LongPath 由来）と、既知の不具合では説明できない欠落とを区別して標準出力に列挙する（要件2.4, 5.2, 5.5、タスク7.2）<br>→ 実効絶対パス長105文字の基準フォルダで generate が完走して終了コード0を返し、旧境界を超える項目も観測されるため、既知の欠落・説明できない欠落・長さのせいで列挙できなかった対象がいずれも0件と報告される（要件2.4, 5.2, 5.5、タスク7.2） | 旧境界（親の絶対パス258文字）から導いた既知の欠落4件・説明できない欠落2件・列挙できなかった対象2件が、その集合どおりに報告される | 3区画はいずれも0件で、旧境界から導いた8項目（4+2+2）がすべて期待値ファイルに記録される。旧境界から導く前提（4・2・2件）、終了コード0、スキップが読み取り拒否フォルダだけであることは変えていない |
| 11 | ScanRunner の失敗分類が、長さ由来の判定に拡張長パス経由の実在確認を用い、実在しないパス・別の型の例外・アクセス拒否を長さ由来と取り違えない（要件2.4、タスク7.2）<br>→ ScanRunner の失敗分類が、長さ由来の判定に実在確認を用い、実在しないパス・別の型の例外・アクセス拒否を長さ由来と取り違えない（要件2.4、タスク7.2） | 前提として、プレーンなパスでは絶対261文字のフォルダが「存在しない」と判定される（これで実在確認が拡張長パス経由であることまで見分けていた） | プレーンなパスでも「存在する」と判定される。拡張長パス経由かどうかはこの項目では見分けられなくなったため、名前からその主張を外した。分類の規則（長さ由来・アクセス拒否・想定外の振り分け、実在しないパスの扱い、属性取得の失敗のガード、再送出）の照合は変えていない |
| 12 | compare が、走査で得た4区画（スキップされた対象・長さのせいで列挙できなかった対象・既知の欠落・説明できない欠落）を generate と同じ件数行で報告し、明細はこの実行の走査に由来する（要件2.4, 5.2、タスク7.4）（名前は変えていない） | 「長さのせいで列挙できなかった対象」が1件以上で、明細はこの実行の基準フォルダの配下。既知の欠落の明細が LongPath の項目と過不足なく一致。LongPath の項目は期待値ファイルのエントリに無い | 「長さのせいで列挙できなかった対象」と既知の欠落はどちらも0件。LongPath の項目はすべて期待値ファイルのエントリにある。件数行が generate と一致すること、スキップの明細がこの実行の基準フォルダ由来であること、判定が一致・終了コード0、残留なしは変えていない |
| 13 | update が、走査で得た4区画を generate と同じ件数行で報告し、その報告が期待値ファイルの書き換え（「更新しました」）より前に出る（要件2.4, 5.2, 5.3, 5.4、タスク7.4）（名前は変えていない） | 12 と同じ補助関数で、「長さのせいで列挙できなかった対象」が1件以上、既知の欠落が LongPath の項目と一致 | 12 と同じく、どちらも0件。報告が書き換えより前に出ること、書き換えが実際に起きること、残留なしは変えていない |

  - 12・13 の期待は、この2件だけが使う補助関数 `AssertScanReportReflectsThisRun` の中で改めた。既知の欠落の明細を導く補助関数 `ExpectedKnownIssueDetails` はこの補助関数だけが使っていたため、使われなくなったので削除した
  - **13件以外は変えていない**: `SelfChecks.cs` の差分の範囲は上の13件の本体と、12・13 だけが使う上記2つの補助関数に限られる（差分の各ハンクがどの項目に属するかを行番号で確かめた）。`Compare/`、`Io/`、`Model/`、`Scan/`、`Fixture/FixtureSpec.cs`、`Fixture/FixtureBuilder.cs`、`Program.cs`、`SelfCheck/` のほかのファイル、`baselines/fixture-v1.golden.txt` は変えていない（期待値データの形式・比較の規則・既知の欠落の判定規則は移行前のまま、要件7.5）
- **確認**（ビルドは順番に実施）

| コマンド | 結果 | 警告 | 終了コード |
|---|---|---|---|
| `dotnet build Tools/GoldenBaseline/GoldenBaseline.csproj --no-incremental` | 成功 | 0 | 0 |
| `GoldenBaseline.exe selfcheck` | 128 件中 0 件が失敗 | — | 0 |
| `GoldenBaseline.exe --help` | 使い方を表示 | — | 0 |
| `GoldenBaseline.exe bogus`（不明なコマンド） | エラー | — | 2 |
| `GoldenBaseline.exe compare --golden baselines/fixture-v1.golden.txt` | 判定: 差分あり（12 件） | — | 1 |
| `dotnet build LargeFolderFinder.csproj -c Debug --no-incremental` | 成功 | 0 | 0 |
| `dotnet build LargeFolderFinder.csproj -c Release --no-incremental` | 成功 | 0 | 0 |

  - `compare` の走査の報告: スキップ 1 件（`access_denied_folder`）、長さのせいで列挙できなかった対象 0 件、既知の欠落 0 件、説明できない欠落 0 件（移行前は既知の欠落 4 件）
  - `compare` の差12件の内訳: `[Unexpected]` 4件（移行前の既知の欠落4件: ASCII の長い連鎖の5階層目のフォルダ `…\eeee…` とその下の `boundary_file.bin`、日本語の長い連鎖の5階層目のフォルダ `…\おおお…` とその下の `境界ファイル.bin`）と、それらを含む親フォルダ8件（各連鎖の1〜4階層目）の `[SizeMismatch]` 期待値=0 実際=8。設計段階の実測（差分12件、既知の4件が Unexpected、親8件のサイズが 0→8）と同じ。期待値データの更新は 2.6 で行う（このタスクでは更新していない）
  - 実行後、`%TEMP%` に `gb_*` の一時フォルダ・ファイルは残っていない

### 2.5 翻訳の網羅の検証ツールの移行（2026-09-19、コミット 3917ed3 の上で実施）
- **対象の変更**: `Tools/LocalizationCheck/LocalizationCheck.csproj` の `TargetFramework` を `net48` から `net10.0-windows` に変えた。それ以外（`OutputType`、`Nullable`、`LangVersion`、`AssemblyName`、`RootNamespace`、アプリへの `ProjectReference`）は変えていない。GoldenBaseline（2.4）と同じく、ツール自身には `RuntimeIdentifier` を指定していない。ソースコード（`Program.cs`、`Check/`、`Io/`、`Model/`、`SelfCheck/`）は変えていない（入口・終了コード・判定規則は移行前のまま、要件7.5）
  - 変更前（アプリだけ net10、ツールは net48）のツールのビルド: エラー2件（`NU1201` プロジェクト LargeFolderFinder は net48 と互換性がない。net48 と net48/win-x86 の各1件）。これが 2.1〜2.4 の間ソリューション全体のビルドが通らなかった原因で、対象を変えるだけで解消した。コードの修正は要らなかった（設計どおり）
  - ツールの出力先は `Tools/LocalizationCheck/bin/<構成>/net10.0-windows/`（移行前は `bin/<構成>/net48/`）
- **期待するキーの一覧の取得元**: ツールは `Enum.GetNames(typeof(LanguageKey))` で、`ProjectReference` によりツールの出力先へコピーされたアプリの `LargeFolderFinder.dll` から一覧を得る。移行後の解決は次のとおりで、アプリの新しい出力先（RID 付き）でも成り立つことを確かめた
  - コンパイル時の参照: `obj/<構成>/net10.0-windows/win-x64/ref/LargeFolderFinder.dll`（アプリの参照アセンブリ）
  - 実行時にコピーされる実体: `bin/<構成>/net10.0-windows/win-x64/LargeFolderFinder.dll`。ツールの出力先の `LargeFolderFinder.dll` とバイト単位で一致した（`cmp`）
  - steering の従来の手順（`-p:BuildProjectReferences=false`、アプリを先に Debug でビルド）でも同じ場所に解決され、警告0・エラー0でビルドでき、`check` はキー 81 を報告した。steering のコマンドのパス（`net48`）の書き換えは 5.2 で行う
  - 言語フォルダは従来どおり、実行ファイルの位置から親へたどってソリューションファイルのあるフォルダの `Resources/Languages` を使う（出力先が1階層変わっても、祖先にリポジトリのルートがあるので影響しない）
- **確認**（ビルドは順番に実施）

| コマンド | 結果 | 警告 | 終了コード |
|---|---|---|---|
| `dotnet build Tools/LocalizationCheck/LocalizationCheck.csproj -c Debug --no-incremental` | 成功 | 0 | 0 |
| `LocalizationCheck.exe selfcheck`（Debug） | 59 件中 0 件が失敗 | — | 0 |
| `LocalizationCheck.exe check`（Debug） | 問題はありません（言語 13、キー 81） | — | 0 |
| `dotnet build Tools/LocalizationCheck/LocalizationCheck.csproj -c Debug -p:BuildProjectReferences=false --no-incremental` | 成功 | 0 | 0 |
| `LocalizationCheck.exe bogus`（不明なコマンド） | 未知のコマンドです: bogus | — | 2 |
| `LocalizationCheck.exe check --dir D:/nonexistent_lc_dir` | 言語フォルダがありません | — | 2 |
| `dotnet build LargeFolderFinder.sln -c Debug --no-incremental` | 成功（3プロジェクト） | 0 | 0 |
| `dotnet build LargeFolderFinder.sln -c Release --no-incremental` | 成功（3プロジェクト） | 0 | 0 |
| `LocalizationCheck.exe selfcheck`（Release） | 59 件中 0 件が失敗 | — | 0 |
| `LocalizationCheck.exe check`（Release） | 問題はありません（言語 13、キー 81） | — | 0 |
| `GoldenBaseline.exe selfcheck`（Debug、ソリューションのビルド後） | 128 件中 0 件が失敗 | — | 0 |

  - ソリューション全体のビルドは、2.1 以降で初めて通った（Implementation Notes の「2.1〜2.4 の間はソリューション全体のビルドが通らない」の解消）
  - 自己検証の結果は設計段階の実測（59件成功、YamlDotNet 18.1.0 でも同じ）と一致した
  - `LocalizationCheck` は一時フォルダを使わない。GoldenBaseline の自己検証の後も `%TEMP%` に `gb_*` は残っていない
  - 移行前の出力 `Tools/LocalizationCheck/bin/Debug/net48/` は無視対象のフォルダに残っている（リポジトリの差分には現れない）

### 2.6 走査結果の差の確認と期待値データの更新（2026-09-19、コミット ab8e01c の上で実施）
- **更新前の比較**: `GoldenBaseline.exe compare --golden baselines/fixture-v1.golden.txt`（Debug、ソリューションのビルド後）は終了コード 1。走査の報告はスキップ 1 件（`access_denied_folder`）、長さのせいで列挙できなかった対象 0 件、既知の欠落 0 件、説明できない欠落 0 件、フィクスチャは「すべての項目を生成しました。未生成の項目はありません。」。比較の出力の差の行の全文は次のとおり（`update` が書き換えの前に出した「[更新前の差分]」も同じ12行だった）

```
[比較]
判定: 差分あり（12 件）
  - [SizeMismatch] aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa 期待値=0 実際=8
  - [SizeMismatch] aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb 期待値=0 実際=8
  - [SizeMismatch] aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\cccccccccccccccccccccccccccccccccccccccccccccccccc 期待値=0 実際=8
  - [SizeMismatch] aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\cccccccccccccccccccccccccccccccccccccccccccccccccc\dddddddddddddddddddddddddddddddddddddddddddddddddd 期待値=0 実際=8
  - [Unexpected] aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\cccccccccccccccccccccccccccccccccccccccccccccccccc\dddddddddddddddddddddddddddddddddddddddddddddddddd\eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee 期待値= 実際=
  - [Unexpected] aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\cccccccccccccccccccccccccccccccccccccccccccccccccc\dddddddddddddddddddddddddddddddddddddddddddddddddd\eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee\boundary_file.bin 期待値= 実際=
  - [SizeMismatch] ああああああああああああああああああああああああああああああああああああああああああああああああああ 期待値=0 実際=8
  - [SizeMismatch] ああああああああああああああああああああああああああああああああああああああああああああああああああ\いいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいい 期待値=0 実際=8
  - [SizeMismatch] ああああああああああああああああああああああああああああああああああああああああああああああああああ\いいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいい\うううううううううううううううううううううううううううううううううううううううううううううううううう 期待値=0 実際=8
  - [SizeMismatch] ああああああああああああああああああああああああああああああああああああああああああああああああああ\いいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいい\うううううううううううううううううううううううううううううううううううううううううううううううううう\ええええええええええええええええええええええええええええええええええええええええええええええええええ 期待値=0 実際=8
  - [Unexpected] ああああああああああああああああああああああああああああああああああああああああああああああああああ\いいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいい\うううううううううううううううううううううううううううううううううううううううううううううううううう\ええええええええええええええええええええええええええええええええええええええええええええええええええ\おおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおお 期待値= 実際=
  - [Unexpected] ああああああああああああああああああああああああああああああああああああああああああああああああああ\いいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいいい\うううううううううううううううううううううううううううううううううううううううううううううううううう\ええええええええええええええええええええええええええええええええええええええええええええええええええ\おおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおおお\境界ファイル.bin 期待値= 実際=
```

- **各差の説明**（フィクスチャ定義 `Fixture/FixtureSpec.cs` の `FixtureSpec.Standard` との対応。2つの長い連鎖は、どちらも1階層50文字×5階層の最深フォルダの直下に 8 バイトのファイルを1件置く定義）

| # | 差の種類 | 項目（相対パス） | 定義の項目 | 説明 |
|---|---|---|---|---|
| 1〜4 | `[SizeMismatch]` 期待値=0 実際=8 | `a…`、`a…\b…`、`a…\b…\c…`、`a…\b…\c…\d…`（相対 50・101・152・203 文字） | ASCII の長い連鎖の1〜4階層目（トレイト Ordinary） | 移行前は5階層目より下を列挙できず、配下の `boundary_file.bin`（8 バイト）がサイズに算入されなかった。移行後は算入され 8 になった |
| 5 | `[Unexpected]` | `a…\b…\c…\d…\e…`（相対254文字） | ASCII の長い連鎖の5階層目のフォルダ（トレイト LongPath） | 移行前の既知の欠落。移行後は観測される |
| 6 | `[Unexpected]` | `a…\e…\boundary_file.bin`（相対272文字） | ASCII の長い連鎖のファイル（トレイト LongPath、8 バイト） | 移行前の既知の欠落。移行後は観測される |
| 7〜10 | `[SizeMismatch]` 期待値=0 実際=8 | `あ…`、`あ…\い…`、`あ…\い…\う…`、`あ…\い…\う…\え…`（相対 50・101・152・203 文字） | 日本語の長い連鎖の1〜4階層目（トレイト Japanese） | 1〜4 と同じ理由。配下の `境界ファイル.bin`（8 バイト）が算入された |
| 11 | `[Unexpected]` | `あ…\い…\う…\え…\お…`（相対254文字） | 日本語の長い連鎖の5階層目のフォルダ（トレイト Japanese・LongPath） | 移行前の既知の欠落。移行後は観測される |
| 12 | `[Unexpected]` | `あ…\お…\境界ファイル.bin`（相対265文字） | 日本語の長い連鎖のファイル（トレイト Japanese・LongPath、8 バイト） | 移行前の既知の欠落。移行後は観測される |

  - `[Unexpected]` の4件は、定義で LongPath のトレイトを持つ項目（4件）と過不足なく一致し、移行前の期待値データで「既知の欠落（境界条件に由来）: 4 件」として扱われていたものと同じ。`[SizeMismatch]` の8件は、そのいずれかを含む祖先フォルダ（両連鎖の1〜4階層目、計8件）と過不足なく一致し、差の大きさ（8）は配下の境界ファイルの定義上のサイズと一致する
  - これ以外の差（`[Missing]`、種別の違い、`normal`・`日本語フォルダ`・`empty_folder`・`access_denied_folder` とその配下のサイズの違い、ヘッダの設定の不一致）は無かった。要件4.1 の想定どおりで、移行に伴う不具合としての差（要件4.2）は見つからなかった。設計段階の実測、2.4 の結果とも同じ
- **更新**（要件4.3）: 既存の `GoldenBaseline.exe update --golden baselines/fixture-v1.golden.txt` で更新した（手で編集していない）。`update` は書き換えの前に走査の報告と「[更新前の差分]」（上の12行と同じ）を出し、「期待値ファイルを更新しました: baselines/fixture-v1.golden.txt（エントリ数: 21）」で終了コード 1（差分ありの判定をそのまま返す既存の動作）
  - 期待値データの変化: エントリ 17 → 21（上の `[Unexpected]` 4件が加わった）、両連鎖の1〜4階層目のサイズが 0 → 8、`# GeneratedAt` が生成時刻に変わった。`# FormatVersion: 2`、`# BaseFolderPathLength: 80`、`# UsePhysicalSize: false`、`# ClusterSizeInBytes: 0`、`# FixtureComplete: true` と、それ以外のエントリは変わっていない。改行は LF のまま（`.gitattributes` の指定どおり）
  - **更新の理由**: .NET 10 では長いパスを作成・列挙できるため、移行前（.NET Framework 4.8）に「親フォルダの絶対パスが258文字以上だと直下を一覧できない」ことで欠けていた4件が走査結果に現れるようになった。差はこの既知の欠落が解消したことと、それによる親フォルダのサイズの変化だけであることを上で確かめたので、移行後の走査結果を今後の基準にする。更新後の期待値では既知の欠落は0件になる。フォルダ数の事前カウントが長いパスで数え損ねる不具合は、本スペックでは直さず `scan-correctness` に残す（要件4.5。走査の処理には触れていない）
- **自己検証の14件目の改訂（設計との差）**: 設計（design.md GoldenBaselineTool）が書き直しの対象としたのは、net48 の制約を前提にした13件（2.4 で改訂済み）だった。これとは別に、**コミット済みの期待値データ**を読んで、長いパスの項目が記録されていないことを確かめる項目が1件あった。この項目は 2.4 の時点では期待値データが移行前のままだったので成功しており、設計の13件には含まれていなかったが、期待値データを更新すると失敗する。13件と同じく net48 の制約を前提にした期待なので、移行後の事実に合わせて改めた（2.4 の実装時に判明し、tasks.md の Implementation Notes に記載済み）
  - RED の観測: 期待値データの更新後、改める前の `selfcheck` は「128 件中 1 件が失敗しました。」（終了コード 1）。失敗したのはこの項目だけで、理由は「文字数境界を超える項目 'a…\e…'（254文字）がコミット済みの期待値に記録されています（現行版の挙動と異なります）。」

| # | 旧い名前 → 新しい名前 | 旧い期待 | 新しい期待 |
|---|---|---|---|
| 14 | コミット済みの期待値 baselines/fixture-v1.golden.txt が形式バージョン2・基準フォルダの実効絶対パス長80文字で記録されており、そこに文字数境界を超える項目（日本語を含む長いパスのフォルダ・ファイルを含む）が現行版の挙動どおり記録されておらず、その記録から KnownIssueAnalyzer が根拠 LongPath とともに列挙する（要件2.3, 5.1, 5.2, 7.1、タスク6.3, 7.3）<br>→ コミット済みの期待値 baselines/fixture-v1.golden.txt が形式バージョン2・基準フォルダの実効絶対パス長80文字で記録されており、そこに文字数境界を超える項目（日本語を含む長いパスのフォルダ・ファイルを含む）が移行後の挙動どおり定義の種別とサイズで記録され、それらを含む祖先フォルダのサイズにも算入されており、その記録から KnownIssueAnalyzer が根拠 LongPath の既知の欠落を列挙しない（要件2.3, 5.1, 5.2, 7.1、タスク6.3, 7.3） | 境界（フォルダ248・ファイル260文字）を超える LongPath の各項目が期待値に記録されておらず、境界内の最も近い祖先フォルダは記録されている。KnownIssueAnalyzer がそれらの各項目を根拠 LongPath の既知の欠落として列挙する | 境界を超える各項目（ASCII・日本語のフォルダとファイル、計4件）が記録され、種別が定義と一致し、サイズが定義から導いた値（ファイルは内容サイズ 8、フォルダは配下のファイルの合計 8）と一致する。各項目の祖先フォルダ（境界内の1〜4階層目を含む連鎖のすべて）も記録され、サイズが定義から導いた値と一致し、境界を超える項目のサイズがそれに算入されている。KnownIssueAnalyzer はそれらの項目を列挙せず、根拠 LongPath の既知の欠落は0件 |

  - 変えていないもの: 形式バージョン2・実効絶対パス長80文字の照合（生のテキストと読み取り後の値の双方）、`generate` の固定値との一致、論理名、`FixtureComplete`、定義に境界を超えるフォルダ・ファイル（ASCII・日本語）が揃っていることの前提、境界内の祖先フォルダが定義にあることの確認。サイズの照合の前提として、期待値が物理サイズ換算なしで記録されていることの確認を加えた
  - 検証の強さ: 旧い期待は「記録されていない」ことと「列挙される」ことを見ていたが、新しい期待は4件の具体的な項目について記録の有無・種別・サイズまで照合し、祖先フォルダ8件のサイズ（旧期待値データでは 0 だった箇所）も照合する。改めた項目が旧い期待値データを誤って通さないことを、旧い期待値データを一時的に戻して `selfcheck` を実行し、この項目だけが「文字数境界を超える項目 'a…\e…'（254文字）がコミット済みの期待値に記録されていません（移行後の挙動と異なります）。」で失敗する（128 件中 1 件が失敗、終了コード 1）ことで確かめ、更新後の期待値データに戻した（`cmp` で一致を確認）
  - **この1件以外は変えていない**: `SelfChecks.cs` の差分はこの項目の名前と本体の範囲（3588〜3721行付近）に限られる。`Compare/`、`Io/`、`Model/`、`Scan/`、`Fixture/`、`Program.cs` は変えていない（期待値データの形式・比較の規則・既知の欠落の判定規則は変えていない、要件7.5）
- **確認**（ビルドは順番に実施）

| コマンド | 結果 | 警告 | 終了コード |
|---|---|---|---|
| `GoldenBaseline.exe compare --golden baselines/fixture-v1.golden.txt`（更新前、Debug） | 判定: 差分あり（12 件） | — | 1 |
| `GoldenBaseline.exe update --golden baselines/fixture-v1.golden.txt`（Debug） | 期待値ファイルを更新しました（エントリ数: 21） | — | 1 |
| `GoldenBaseline.exe compare --golden baselines/fixture-v1.golden.txt`（更新後、Debug） | 判定: 一致 | — | 0 |
| `GoldenBaseline.exe selfcheck`（更新後・改訂前、Debug） | 128 件中 1 件が失敗（上記の項目） | — | 1 |
| `dotnet build LargeFolderFinder.sln -c Debug --no-incremental` | 成功 | 0 | 0 |
| `GoldenBaseline.exe selfcheck`（改訂後、Debug） | 128 件中 0 件が失敗 | — | 0 |
| `dotnet build LargeFolderFinder.sln -c Release --no-incremental` | 成功 | 0 | 0 |
| `GoldenBaseline.exe selfcheck`（Release） | 128 件中 0 件が失敗 | — | 0 |
| `GoldenBaseline.exe compare --golden baselines/fixture-v1.golden.txt`（Release） | 判定: 一致 | — | 0 |
| `LocalizationCheck.exe check`（Debug・Release） | 問題はありません（言語 13、キー 81） | — | 0 |

  - 実行後、`%TEMP%` に `gb_*` の一時フォルダ・ファイルは残っていない

### 3.1 2形態を発行するスクリプト（2026-09-19、コミット 4900ba9 の上で実施）
- **作ったもの**: `build/Publish.ps1`（Windows PowerShell 5.1 で動くよう UTF-8 BOM 付き・CRLF で保存）。設計（design.md PublishScript）どおり、引数は `-Form SelfContained | FrameworkDependent`（必須）、`-OutputDir`（省略時 `artifacts/publish/<Form>`）、`-Configuration Release | Debug`（既定 Release）
  - 自己完結: `dotnet publish LargeFolderFinder.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o <OutputDir>`
  - フレームワーク依存: `dotnet publish LargeFolderFinder.csproj -c Release -r win-x64 --no-self-contained -p:PublishSingleFile=true -o <OutputDir>`（`--self-contained false` は使わない。dotnet/sdk#51888）
  - 単一ファイルの圧縮（`EnableCompressionInSingleFile`）は指定しない（既定の無効のまま）。csproj は変えていない
  - 使い方: `powershell -NoProfile -ExecutionPolicy Bypass -File build/Publish.ps1 -Form SelfContained`（相対の `-OutputDir` は現在の場所が基準）
- **失敗の扱い**（すべて終了コード 1、標準エラーに「エラー: …」）: `-Form` の省略・不正な値、`-Configuration` の不正な値（`dotnet` を呼ぶ前に止める。大文字小文字の違いは許す）、`dotnet` が無い、`dotnet publish` の失敗（その終了コードを表示）、`dotnet publish` が成功を返しても `LargeFolderFinder.exe` が無い、途中の例外
- **設計に無い判断（発行先の扱い）**: 前回の発行物が残ったままだと成功と取り違えるため、`artifacts/publish` 配下の発行先は発行の前に消して作り直す。それ以外の場所は利用者のファイルを消さないよう、存在しないか空のフォルダだけを受け付け、空でなければ何も消さずに失敗にする（`artifacts/legacy-net48` などの控えには触れない）
- **引数の検証の試験**（RED→GREEN）: 偽の `dotnet`（受け取った引数を記録し、指定の終了コードで終わる）を PATH の先頭に置いて8項目を確かめた（試験の道具はリポジトリの外に置き、コミットしない）。スクリプトを作る前は8件中3件が失敗（`dotnet` の失敗の伝播、両形態の引数。スクリプトが無いため）、作った後は8件中0件が失敗
  - 項目: 不正な形態・形態の省略・不正な構成で非0かつ `dotnet` を呼ばない／`dotnet` の失敗で非0／フレームワーク依存の引数が `--no-self-contained` を含み `--self-contained false` を含まない／自己完結の引数が `--self-contained true`・単一ファイル・`-r win-x64` を含み圧縮を含まない／`dotnet` が成功でも exe が無ければ非0／`artifacts/publish` の外の空でない発行先で非0かつ中身を消さない
- **実際の発行**（SDK 10.0.401、Release、順番に実施。アプリは起動していない）

| 形態 | 発行先 | `LargeFolderFinder.exe` | ProductVersion | FileVersion | 終了コード |
|---|---|---|---|---|---|
| 自己完結 | `artifacts/publish/SelfContained` | 140,586,265 バイト（約140.6MB） | `1.0.3` | `1.0.3.0` | 0 |
| フレームワーク依存 | `artifacts/publish/FrameworkDependent` | 1,052,929 バイト（約1.05MB） | `1.0.3` | `1.0.3.0` | 0 |

  - 両形態とも exe の隣に `Config.txt`（234 バイト）、`Resources/Languages/*.yaml` 13本、`Resources/Readme/Readme_*.txt` 13本、`Resources/License/`（`LICENSE.txt`、`ThirdPartyNotices.txt`）の2本、`LargeFolderFinder.pdb`（75,868 バイト）が出た。exe 以外のファイルの一覧と大きさは両形態で同じ。pdb を zip に入れないのは 3.3 の仕事
  - 版の文字列はコミットハッシュが付かない `X.Y.Z` の形（要件2.6）
  - 設計段階の実測（自己完結 140.8MB、フレームワーク依存 1.28MB）と比べ、フレームワーク依存版が約0.23MB 小さい（差の理由は調べていない）が、いずれも「約140MB」「約1MB」の範囲に収まる
  - 自己完結版を形態の名前の大文字小文字を変えて（`-Form selfcontained`）もう一度発行し、前回の発行先を消して作り直して同じ大きさになること、フレームワーク依存版の発行を挟んでも自己完結版が 140,586,265 バイトになることを確かめた

### 3.2 exe の起動を確かめるスクリプト（2026-09-19、コミット 64b26f2 の上で実施）
- **作ったもの**: `build/Test-Launch.ps1`（`Publish.ps1` と同じく Windows PowerShell 5.1 で動くよう UTF-8 BOM 付き・CRLF で保存）。設計（design.md LaunchCheck）どおり、引数は `-ExePath`（必須）と `-TimeoutSeconds`（既定 20、1〜600 の整数）
  - 使い方: `powershell -NoProfile -ExecutionPolicy Bypass -File build/Test-Launch.ps1 -ExePath artifacts/publish/SelfContained/LargeFolderFinder.exe`（相対パスは現在の場所が基準）
- **起動確認の方式**
  - exe を `Process.Start` で直接起動する（シェルを介さない。標準出力・標準エラーは非同期で読み、失敗の報告に添える）。以後はこのとき得たプロセスだけを扱い、名前でプロセスを探すことはしない
  - ウィンドウの検出: `EnumWindows` でそのプロセスの最上位ウィンドウを列挙し、**表示中で、クラス名が `HwndWrapper` で始まる**（WPF のウィンドウ）ものが出たら「ウィンドウが出た」とする。0.2 秒ごとに確かめ、`-TimeoutSeconds` の間待つ
  - ウィンドウが出たら 2 秒待ち、プロセスが生きていてエラーのダイアログが出ていないことを確かめる（「生きていること」の確認）
  - 閉じる要求: 見つけたウィンドウに `PostMessage(WM_CLOSE)` を送り、`-TimeoutSeconds` の間終了を待つ。終わらなければ警告を出し、自分が起動したプロセスだけを `Process.Kill()` で終了させる（起動の確認そのものは成功として扱う）。スクリプトがどこで抜けても `finally` で同じ片付けをする
- **失敗の扱い**（すべて終了コード 1、標準エラーに「エラー: …」。exe の標準エラー・標準出力と終了コードを添える）: `-ExePath` の省略、`-TimeoutSeconds` の不正な値、exe が無い、exe を起動できない（壊れたファイルなど）、ウィンドウが出る前の終了、時間内にウィンドウが出ない、エラーのダイアログが出た、途中の例外
- **設計に無い判断（エラーのダイアログを失敗にする）**: アプリは初期化に失敗すると `MessageBox` を出したまま生き続け（`MainWindow` のコンストラクタの `catch`、`App.xaml.cs` の未処理例外の処理）、フレームワーク依存版はランタイムが無いとホストが案内のダイアログを出す。どちらも「ウィンドウが出た」と取り違えないよう、`Process.MainWindowHandle` ではなくクラス名で WPF のウィンドウを見分け、表示中のダイアログ（クラス名 `#32770`）が出たら、その題と文言を添えて失敗にする
- **試験**（RED→GREEN。試験の道具と試験用の exe はリポジトリの外に置き、コミットしない）: 試験用の exe を `Add-Type -OutputAssembly` で作り、各場合の終了コード・出力の文言・試験後に試験対象のプロセスが残っていないことを確かめた。スクリプトを作る前は9件中9件が失敗、作った後は11件中0件が失敗（最後の通しの実行でも11件中0件）

| 場合 | 使った exe | 結果 | 終了コード | 所要 |
|---|---|---|---|---|
| 引数なし | — | 「-ExePath に…指定してください」 | 1 | 0.2 秒 |
| 存在しない exe | — | 「exe が見つかりません」 | 1 | 0.3 秒 |
| 待つ時間が 0 | — | 「-TimeoutSeconds の値 '0' は使えません」 | 1 | 0.2 秒 |
| 壊れた exe | 中身が文字列だけの `LargeFolderFinder.exe` | 「exe を起動できません（The specified executable is not a valid application for this OS platform.）」 | 1 | 0.5 秒 |
| ウィンドウを出す前に終わる | 標準エラーに書いて終了コード 3 で終わる exe | 「ウィンドウが出る前にプロセスが終了しました（終了コード 3）」と標準エラーの内容 | 1 | 0.7 秒 |
| ウィンドウを出さずに生き続ける | 待つだけの exe（`-TimeoutSeconds 4`） | 「時間内（4 秒）にウィンドウが出ませんでした」。起動したプロセスを終了させた | 1 | 4.6 秒 |
| エラーのダイアログを出す | `MessageBox` を出して待つ exe | 「起動中にエラーのダイアログが出ました」とダイアログの題・文言。起動したプロセスを終了させた | 1 | 0.8 秒 |
| WPF の窓（正常に閉じる） | 窓を1つ出す WPF の exe | 閉じる要求で終了（終了コード 0） | 0 | 3.1 秒 |
| WPF の窓（閉じるのを拒む） | `Closing` を取り消す WPF の exe（`-TimeoutSeconds 3`） | 警告を出し、起動したプロセスだけを終了させた | 0 | 6.1 秒 |
| 自己完結版 | `artifacts/publish/SelfContained/LargeFolderFinder.exe`（3.1 の発行物） | 起動から約1.5秒でウィンドウ（題 `Large Folder Finder`、クラス `HwndWrapper[LargeFolderFinder;;…]`）、閉じる要求で終了（終了コード 0） | 0 | 4.3〜10.4 秒 |
| フレームワーク依存版 | `artifacts/publish/FrameworkDependent/LargeFolderFinder.exe`（3.1 の発行物） | 起動から約1.6秒でウィンドウ、閉じる要求で終了（終了コード 0） | 0 | 9.8〜10.3 秒 |

  - 閉じる要求から終了まで最大で約5.7秒かかった（終了時に設定とセッションを保存するため。手元のアプリデータには 36MB のセッションがある）。既定の 20 秒に収まる
  - 試験の前後で `tasklist` に LargeFolderFinder は無く、試験後に試験対象のプロセスは残っていない
- **手元での試験時のアプリデータの扱い**（スクリプト自身は退避・復元をしない。設計どおり、スクリプトの説明と実行時の表示で案内する）
  1. `tasklist` で LargeFolderFinder が起動していないことを確かめる
  2. `%LOCALAPPDATA%\Cat & Chocolate Laboratory\LargeFolderFinder`（`AppConstants.AppDataDirectory`）をフォルダごとリポジトリの外に複製し、全ファイルの相対パス・大きさ・SHA-256 の一覧を控える（今回は20項目: `Cache.txt`、`Settings.msgpack`、`Logs/` のログ8本、`Sessions/` のセッション8本とフォルダ2つ）
  3. 起動確認を行う。今回の試験では、アプリが `Settings.msgpack` と開いていたセッション1本を保存し直し、ログを3本書いて古いログ3本を消した（ログの上限 4 本の整理）。組織のフォルダの下に別のフォルダは増えていない
  4. `robocopy <退避先> <アプリデータ> /MIR /COPY:DAT /DCOPY:DAT` で退避の状態に戻し（増えたログは消える）、一覧を取り直して控えと突き合わせる。今回は差 0 件（前後とも20項目、一覧のハッシュ `C37A4133…3D42DF` が一致）

### 3.3 配布用の zip を作るスクリプト（2026-09-19、コミット 36f94e0 の上で実施）
- **作ったもの**: `build/Package.ps1`（`Publish.ps1`・`Test-Launch.ps1` と同じく Windows PowerShell 5.1 で動くよう UTF-8 BOM 付き・CRLF で保存）。設計（design.md PackageScript）どおり、引数は `-SelfContainedDir`、`-FrameworkDependentDir`、`-OutputDir`（省略時 `artifacts/package`）
  - 使い方: `powershell -NoProfile -ExecutionPolicy Bypass -File build/Package.ps1`（相対パスは現在の場所が基準）
  - 出力: `LargeFolderFinder.zip`（自己完結、既定の配布物）と `LargeFolderFinder-FrameworkDependent.zip`（軽量版）
  - 入れるもの: `LargeFolderFinder.exe`、`Config.txt`、`Resources/Languages/*.yaml`、`Resources/Readme/*.txt`、`Resources/License/*`（直下のファイル）。それ以外（pdb、下位フォルダなど）は入れず、入れなかったファイルの名前を表示する
- **検査**（満たさなければ終了コード 1、標準エラーに「エラー: …」）。zip を作る前（発行フォルダから選んだ一覧）と、作った後（zip を読み直した一覧）の両方で行う
  - 必須のファイル: exe、`Config.txt`、言語ファイル13本、`Resources/License/LICENSE.txt`・`ThirdPartyNotices.txt`（設計どおり Readme は必須に含めないが、2つの一覧の一致の検査の対象には含める）
  - 2つの一覧（zip 内のパスと大きさ）が exe の大きさを除いて一致すること。違いは「自己完結版にだけある」「軽量版にだけある」「大きさが違う」の形で全件を示す
  - zip を読み直したとき、構成に当てはまらない項目や `\` を含む項目が無いこと、zip の中身（パスと大きさ）が発行フォルダから選んだ一覧と同じであること
- **失敗の扱い**（すべて終了コード 1）: 発行フォルダが無い（「先に build/Publish.ps1 で発行してください」）、上記の検査の失敗、出力先がフォルダでない、途中の例外。検査に失敗したときは、そのスクリプトが作った zip を消す（発行フォルダの検査で失敗したときは zip を作らない）
- **設計に無い判断**
  - 発行フォルダの引数の省略時は `artifacts/publish/<形態>`（`Publish.ps1` の既定の発行先）とした
  - 出力先の扱いは `Publish.ps1` にそろえた。`artifacts/package` 配下では前回の同じ名前の zip を消して作り直す。それ以外の場所では、同じ名前の zip があれば何も消さずに失敗にする
  - 言語ファイルは名前ではなく本数（13本）で確かめる（`LocalizationCheck` の「言語 13」と同じ数）
- **zip の作り方**: `Compress-Archive` は使わず、`System.IO.Compression` の `ZipFile.Open` と `CreateEntryFromFile` で、zip 内のパスを `/` 区切りで明示して作る。手元の Windows PowerShell 5.1 の `Microsoft.PowerShell.Archive` は 1.0.1.0 で、zip 内のパスの区切りに `\` を書く既知の問題がある版のため
  - 名前のエンコーディングは指定しない（既定のまま）。既定では ASCII だけの名前はそのまま、ASCII 以外の文字を含む名前は UTF-8 で書いて UTF-8 の印（汎用フラグのビット 11）を付ける。試作で UTF-8 を明示して渡したところ、.NET Framework は印を付けずに UTF-8 で書き、Python の `zipfile` で読むと `Readme_µùÑµ£¼Φ¬₧.txt` と化けた（CP437 として解釈される）ため、明示しない形にした。現在の配布物の名前はすべて ASCII なので、印の付いた項目は無い
- **試験**（RED→GREEN。試験の道具はリポジトリの外に置き、コミットしない。試験用の発行フォルダは軽量版の発行物を複製して作り、`artifacts/publish` には触れていない）: スクリプトを作る前は10件中10件が失敗（スクリプトが無いため）、作った後は17件中0件が失敗

| 場合 | 結果 | 終了コード |
|---|---|---|
| 3.1 の2つの発行フォルダ | zip を2つ作成。各30項目、pdb なし、区切りは `/`、exe 以外の一覧と大きさが一致、自己完結版の exe は 140,586,265 バイト | 0 |
| 自己完結版から `Resources/License/LICENSE.txt` を消す | 「発行フォルダの検査に失敗しました」「自己完結版に必須のファイルがありません: Resources/License/LICENSE.txt」「軽量版にだけある: …」。zip は作らない | 1 |
| 軽量版から `Config.txt` を消す | 「軽量版に必須のファイルがありません: Config.txt」ほか。zip は作らない | 1 |
| 両方から `tr.yaml` を消す（一覧は一致） | 「言語ファイル（Resources/Languages/*.yaml）が 12 本です（13 本が必要）」。zip は作らない | 1 |
| 自己完結版から exe を消す | 「自己完結版に必須のファイルがありません: LargeFolderFinder.exe」ほか。zip は作らない | 1 |
| 軽量版の `Readme_en.txt` に1行足す | 「大きさが違う: Resources/Readme/Readme_en.txt（自己完結版 5975 バイト、軽量版 5978 バイト）」。zip は作らない | 1 |
| 自己完結版から `Readme_ja.txt` を消す | 「軽量版にだけある: Resources/Readme/Readme_ja.txt」。zip は作らない | 1 |
| 発行フォルダが無い | 「自己完結版の発行フォルダが見つかりません: …（先に build/Publish.ps1 で発行してください）」 | 1 |
| 自己完結版に `extra.dll` と `Resources/Languages/sub/xx.yaml` を足す | どちらも入れずに作成（30項目） | 0 |
| `artifacts/package` の外の出力先に同じ名前のファイルがある | 「出力先に同じ名前のファイルが既にあります」。既存のファイルは変えず、もう一方の zip も作らない | 1 |
| 両方に `Readme_日本語.txt` を足す | 作成。その項目だけに UTF-8 の印が付き（他の30項目は印なし）、読み直すと同じ名前 | 0 |

- **実際の梱包**（3.1 の発行物から、既定の引数で。2回目は前回の zip を消して作り直す経路を通った）

| zip | 大きさ | 項目数 | exe の大きさ |
|---|---|---|---|
| `artifacts/package/LargeFolderFinder.zip`（自己完結） | 59,197,436 バイト（約59.2MB） | 30 | 140,586,265 バイト |
| `artifacts/package/LargeFolderFinder-FrameworkDependent.zip`（軽量版） | 492,144 バイト（約0.49MB） | 30 | 1,052,929 バイト |

  - 中身（両方で同じ。exe を除き大きさも同じ）: `Config.txt`（234）、`LargeFolderFinder.exe`、`Resources/Languages/` の de・en・es・fr・hi・it・ja・ko・pt-BR・ru・tr・zh-CN・zh-TW の `.yaml` 13本、`Resources/License/LICENSE.txt`（1,119）・`ThirdPartyNotices.txt`（8,057）、`Resources/Readme/Readme_<言語>.txt` 13本。`LargeFolderFinder.pdb` は入れていない
  - Python の `zipfile` で読み直し、両方とも区切りに `\` を含む項目が0件、UTF-8 の印の付いた項目が0件（名前がすべて ASCII のため）、圧縮方式は Deflate、`testzip()` で壊れた項目なしを確かめた
  - `Expand-Archive` で展開し、30ファイルすべてが発行フォルダのファイルと SHA-256 で一致することを両方の zip で確かめた

### 4.1 テストプロジェクト（2026-09-19、コミット 8bdcc0d の上で実施）
- **作ったもの**: `Tests/LargeFolderFinder.Tests/`（`LargeFolderFinder.Tests.csproj`、`ToolRunner.cs`、`GoldenBaselineTests.cs`、`LocalizationTests.cs`）。`LargeFolderFinder.sln` に加えた（構成は既存と同じ `Debug|Any CPU`・`Release|Any CPU` の2つだけ。`dotnet sln add` は x64・x86 の構成を全プロジェクトに足すため使わず、手で加えた）
  - `net10.0-windows`、`OutputType=Exe`、xunit.v3 4.0.1（推移的に xunit.v3.mtp-v2 4.0.1、Microsoft.Testing.Platform 2.4.0）。`global.json` の `test.runner` により Microsoft Testing Platform で走る
  - 2つの検証ツールは `ProjectReference`（`ReferenceOutputAssembly=false`、`Private=false`）で参照し、ビルドの順序と exe の存在だけを保証する。テストの出力フォルダにツールのファイルは複写されない
- **ToolRunner**: テストの出力フォルダから親へたどり、`Tools/<名前>/bin/<構成>/net10.0-windows/<名前>.exe` を探す。構成はテスト自身のアセンブリの `AssemblyConfiguration`（SDK が `$(Configuration)` から付ける）にそろえる。見つけた親フォルダ（リポジトリのルート）を作業フォルダにして子プロセスで実行し、終了コード・標準出力・標準エラー（UTF-8）を返す。見つからない、起動できない、10分で終わらない（自分が起動したプロセスの木だけを終了させる）ときはテストを失敗にする
- **テスト**（8件）

| クラス | テスト | 確かめること |
|---|---|---|
| GoldenBaselineTests | `Compare_MatchesCommittedGolden` | `compare --golden baselines/fixture-v1.golden.txt` が 0 |
| GoldenBaselineTests | `SelfCheck_AllPass` | `selfcheck` が 0 |
| GoldenBaselineTests | `EnvironmentConstraint_*`（4件） | 終了コード 2 をスキップにする判定の規則（下記） |
| LocalizationTests | `Check_NoProblems` | `check` が 0 |
| LocalizationTests | `SelfCheck_AllPass` | `selfcheck` が 0 |

- **環境の制約の扱い**（要件6.4）: 終了コード 0 は成功、1 は失敗。2 のうち、出力に GoldenBaseline の次の文言が含まれるものだけを xunit.v3 の `Assert.Skip` でスキップにし、理由に「成功ではありません」と終了コード・出力の全文を添える。それ以外の 2 は失敗
  - `未生成の項目:`（フィクスチャを生成できなかった報告。基準フォルダを作れないと全項目が未生成になり、続く走査が「基準フォルダが見つかりません」で終了コード 2 になる）
  - `文字に固定できません`（既定の置き場 `%TEMP%` が長すぎて、基準フォルダの実効絶対パス長を 80 文字に固定できない）
  - 設計に無い判断: 印の文言は設計が例に挙げた「生成できなかった項目」とパスの長さの制約を、GoldenBaseline の実際の出力の文言に当てはめて決めた。設定不一致・期待値ファイルを読めない等の 2 は環境の制約と判断できないので失敗にする
- **確かめたこと**
  - ビルド: `dotnet build LargeFolderFinder.sln` を Debug・Release（Release は `--no-incremental` と `-warnaserror` でも）で行い、警告0件・エラー0件
  - `dotnet test --solution LargeFolderFinder.sln -c Release`: 合計 8、成功 8、失敗 0、スキップ 0（テストの時間 約13秒、コマンド全体 約14〜16秒）。Debug でも同じ
  - 期待値データの18行目（`F	normal\file_small.txt	10`）を `11` に変えると、`Compare_MatchesCommittedGolden` が失敗（合計 8、失敗 1、成功 7、`dotnet test` の終了コード 2）。理由に `判定: 差分あり（1 件）`、`[SizeMismatch] normal\file_small.txt 期待値=11 実際=10` が出た。`git restore baselines/fixture-v1.golden.txt` で戻すと再び 8件成功し、期待値データの差分は無い
  - スキップの経路: `TEMP`・`TMP` を 80 文字を超える既存のフォルダにして `Compare_MatchesCommittedGolden` だけを走らせると、GoldenBaseline が「既定の置き場（…）が長すぎるため、基準フォルダの実効絶対パス長を 80 文字に固定できません（名前を最短にしても 163 文字になります）」で終了コード 2 を返し、テストは「スキップされました」（合計 1、成功 0、スキップ 1、`dotnet test` の終了コード 0）。フィクスチャは作られない。「未生成の項目」の経路は手元で安全に再現できない（`dotnet test` 自身も `TEMP` を使うため、作れない場所を `TEMP` にするとテストの基盤が起動しない）ので、実際の文言を与えた判定のテストで確かめた
- **注意**: xunit.v3 の既定でテストのクラスは並列に走る（GoldenBaseline の2件は同じクラスなので順番に走り、LocalizationCheck と並ぶ）。Microsoft Testing Platform は利用統計の送信の部品（Microsoft.Testing.Extensions.Telemetry）を推移的に含む
### 4.2 変更のたびに走る自動ビルド（2026-09-19、コミット 7216922 の上で実施）
- **作ったもの**: `.github/workflows/ci.yml`（CiWorkflow）
  - 契機: すべてのブランチへの push（`branches: ['**']`）と pull request
  - ランナー: `windows-2025`。権限: `contents: read` のみ
  - 手順: `actions/checkout@v7` → `actions/setup-dotnet@v6`（`global-json-file: global.json`）→ `dotnet --version`（使う SDK の版をログに残す）→ `dotnet build LargeFolderFinder.sln -c Release -warnaserror` → `dotnet test --solution LargeFolderFinder.sln -c Release --no-build`。どの手順が失敗してもワークフロー全体が失敗になる
  - アクションの版: 設計は版の固定の方法を指定していないため、メジャーの版のタグで指定した。`setup-dotnet` は research の調査どおり最新メジャーの v6、`checkout` は `git ls-remote` で確かめた最新メジャーの v7（2026-09-19 時点）。どちらも GitHub 公式のアクションで、サードパーティのアクションは使わない
  - `setup-dotnet@v6` は `global.json` の `rollForward: latestPatch` を読んで `10.0.4xx` の最新の SDK を入れる（v6 のソースの `getVersionFromGlobalJson` で確認）。`global.json` の固定（10.0.401 以上の 10.0.4xx）と同じ範囲で、プリインストールの 10.0.400 は条件を満たさないので使われない。実際に使われた版は `dotnet --version` の手順でログに出る
- **設計に無い追加**
  - ワークフローの `env` に `DOTNET_CLI_TELEMETRY_OPTOUT: 1` と `TESTINGPLATFORM_TELEMETRY_OPTOUT: 1` を設定した（dotnet CLI と Microsoft Testing Platform の利用統計の送信を止める。4.1 で判明した推移的な依存への対処）
  - SDK の版を表示する手順（`dotnet --version`）を加えた。`global.json` の固定がランナーで効いていることをログで確かめるため
- **スキップの件数の確かめ方**: `dotnet test` の最後の集計（「合計」「失敗」「成功」「スキップ済み」）がログに出る（ランナーの表示言語では英語になる見込み）。5.3 では、この集計のスキップが 0 件であることを GitHub 上のログで確かめる
- **確かめたこと**（手元、ワークフローと同じ環境変数を設定して、同じコマンドを順番に実行）
  - `dotnet --version`: `10.0.401`、終了コード 0
  - `dotnet build LargeFolderFinder.sln -c Release -warnaserror`: 警告 0、エラー 0、終了コード 0。`--no-incremental` を付けても同じ
  - `dotnet test --solution LargeFolderFinder.sln -c Release --no-build`: 合計 8、失敗 0、成功 8、スキップ 0、終了コード 0
  - YAML の構文: PyYAML（リポジトリの外に一時的に入れたもの）の `safe_load` で読み込め、契機・権限・環境変数・手順が意図どおりの構造になっていることを確かめた。actionlint は手元に無いため使っていない
  - GitHub 上での実行は 5.3 で確かめる
### 4.3 タグから下書きのリリースまでを作る自動ビルド（2026-09-19、コミット b3415c5 の上で実施）
- **作ったもの**: `.github/workflows/release.yml`（ReleaseWorkflow）。ランナー・アクションの版・`env` の利用統計の停止は `ci.yml` にそろえた（`windows-2025`、`actions/checkout@v7`、`actions/setup-dotnet@v6` の `global-json-file`、`DOTNET_CLI_TELEMETRY_OPTOUT`・`TESTINGPLATFORM_TELEMETRY_OPTOUT`）。サードパーティのアクションは使わず、下書きはランナーに入っている `gh` で作る
  - 契機: `v*` のタグの push のみ（`workflow_dispatch` は使わない）
  - 権限: ワークフロー全体は `permissions: {}`、ジョブにだけ `contents: write`。チェックアウトは `persist-credentials: false` にし、書き込みできるトークンを git の設定に残さない（トークンは下書きを作る手順の `GH_TOKEN` にだけ渡す）
  - 手順（1つのジョブで順に行い、どこかで失敗すると以降は走らず下書きは作られない）
    1. タグと版の一致: タグは `^v(\d+\.\d+\.\d+)(-test\.\d+)?$` の形だけを受け付け、`X.Y.Z`（試験用は `-` より前）と `LargeFolderFinder.csproj` の `<Version>`（1つだけで `X.Y.Z` の形であることも確かめる）を比べる。版と試験かどうかを手順の出力に渡す
    2. `dotnet --version` → `dotnet build LargeFolderFinder.sln -c Release -warnaserror` → `dotnet test --solution LargeFolderFinder.sln -c Release --no-build`（`ci.yml` と同じ）
    3. `build/Publish.ps1 -Form SelfContained`、`-Form FrameworkDependent`（`powershell -NoProfile -ExecutionPolicy Bypass -File`。Windows PowerShell 5.1 で動かす）。続けて発行した2つの exe の `ProductVersion` が `^\d+\.\d+\.\d+$` の形であることを確かめる（要件2.6）
    4. `build/Test-Launch.ps1` で2つの exe の起動確認（要件5.2）
    5. `build/Package.ps1` で zip を2つ作る
    6. 下書きのリリース: `gh api --paginate repos/<repo>/releases` で下書きを含む既存のリリースのタグを調べ、同じタグがあれば失敗させる（上書きしない）。無ければ `gh release create <tag> <2つの zip> --draft --verify-tag --title <題> --notes-file <説明>`。公開はしない
  - 試験用のタグ（`vX.Y.Z-test.N`）では下書きの題を `[TEST / 試験] <tag>` にする
  - 下書きの説明（定型文）: 2つの配布物の違い（ランタイムの要否、大きさ、選び方、中身は exe 以外同じ）を表と箇条書きで書く
- **設計に無い判断**
  - 下書きの説明文を英語と日本語の両方で書いた（設計は「定型文」とだけ指定。案内（README）が英語・日本語の両節であることに合わせた）
  - タグの形は `vX.Y.Z` と `vX.Y.Z-test.N` に限り、それ以外（`v1.0.3-rc.1` など）は失敗にした（契機の `v*` は広いため）
  - exe の `ProductVersion` は形に加えて、タグの版と一致することも確かめる
  - `gh release create` が途中で失敗したとき（zip の添付の失敗など）は、そのタグの下書きを API で消してから失敗にする（「どこかで失敗したら下書きを作らない」を満たすため。事前に同じタグのリリースが無いことを確かめているので、消すのはこの手順で作ったものだけ）
  - 起動確認の2つの手順に `timeout-minutes: 10` を付けた。手元の実行で、終了させたアプリのプロセスがカーネル内で止まったまま残り、そのプロセスが受け継いだ出力の管を離さないため、呼び出した側の出力の取り込みが終わらなくなったことがあった（下記）。ランナーで同じことが起きても、ジョブが既定の6時間まで止まらないようにするため
- **確かめたこと**（手元。GitHub 上での実行は 5.3 で確かめる）
  - YAML の構文: PyYAML（リポジトリの外に一時的に入れたもの）の `safe_load` で読み込め、契機・権限・環境変数・既定のシェル・13の手順の順番が意図どおりであることを確かめた。各 `run` を PowerShell の構文解析（`Parser.ParseFile`）にかけ、構文エラー 0 件。下書きの説明文の部分を切り出して実行し、字下げの無い Markdown が生成されることを確かめた。actionlint は手元に無いため使っていない。手元に PowerShell 7（ランナーの既定の `pwsh`）が無いため、`run` の中の PowerShell は Windows PowerShell 5.1 でも動く書き方にして 5.1 で試した
  - タグと版の一致（ワークフローの手順の `run` をそのまま切り出し、`TAG`・`GITHUB_OUTPUT` を与えて実行。ワークフローが無い状態では読み込みで失敗）

| タグ | 終了コード | 出力 |
|---|---|---|
| `v1.0.3` | 0 | `version=1.0.3`、`is_test=false`（「本番のタグです」） |
| `v1.0.3-test.1` | 0 | `version=1.0.3`、`is_test=true`（「試験用のタグです」） |
| `v1.0.4` | 1 | 「タグの版 '1.0.4'（タグ 'v1.0.4'）と LargeFolderFinder.csproj の版 '1.0.3' が一致しません」 |
| `v1.0.3-rc.1` | 1 | 「タグ 'v1.0.3-rc.1' は vX.Y.Z か vX.Y.Z-test.N の形ではありません」 |
| `1.0.3` | 1 | 同上 |

  - GitHub に依存しない手順を同じ順に手元で実行（ワークフローと同じ環境変数。アプリデータは事前に退避）

| 手順 | 結果 | 終了コード |
|---|---|---|
| `dotnet --version` | `10.0.401` | 0 |
| ビルド（`-warnaserror`） | 警告 0、エラー 0 | 0 |
| テスト | 合計 8、失敗 0、成功 8、スキップ 0 | 0 |
| 発行（自己完結・軽量版） | 両方とも成功 | 0、0 |
| 版の文字列（切り出した `run`、期待する版 1.0.3） | 両方とも `1.0.3` | 0 |
| 起動確認（軽量版） | 起動から約1.5秒でウィンドウ、閉じる要求で終了（終了コード 0）、12秒 | 0 |
| 起動確認（自己完結版） | 起動から約1.6秒でウィンドウ、閉じる要求で終了（終了コード 0）、5秒 | 0 |
| zip の作成 | `LargeFolderFinder.zip` 59,197,419 バイト・30項目、`LargeFolderFinder-FrameworkDependent.zip` 492,141 バイト・30項目 | 0 |

  - **1回目の起動確認で起きたこと**: 最初の通しの実行では、自己完結版の起動確認の出力を管で受けていたところ、取り込みが終わらなくなった。起動されたアプリ（PID 58936）は、利用者のアプリデータにある8本のセッションを読み込む途中（ログの最後は `LoadCache: Loading file Scan20260804_1050_42724.msgpack`）で、設定と開いていたセッション1本を保存し直しており（閉じる要求は受けていた）、その後プロセスは「終了済み」だがスレッド1つが残って消えない状態になった（`taskkill` は「実行中のインスタンスがありません」、`Get-Process` は `HasExited=True`、1.3GB・ハンドル1,208 のまま）。起動確認のスクリプト自身は終わっていたが、そのプロセスが受け継いだ出力の管が閉じないため取り込みが止まった。スクリプトの出力は失われ、判定の結果は分からない。出力をファイルに取る形で2つの起動確認をやり直し、上の表のとおりどちらも成功した。原因（利用者のセッションのデータ、ネットワークの場所への参照、セキュリティソフトなど）は調べていない。CI は使い捨ての環境でセッションが無いため、同じ条件にはならない見込み
  - アプリデータの退避と復元: `tasklist` で LargeFolderFinder が起動していないことを確かめ、`%LOCALAPPDATA%\Cat & Chocolate Laboratory\LargeFolderFinder` をリポジトリの外に複製し、全ファイル（17本）の相対パス・大きさ・SHA-256 を控えた。起動確認で `Settings.msgpack` とセッション1本が保存し直され、ログが入れ替わった。`robocopy /MIR` で2回（1回目の起動確認の後と、すべての手順の後）戻し、どちらも控えとの差 0 件（17本、一覧のハッシュ `A9323723…5E57A9` が一致）

### 5.1 利用者向けの案内と著作権表示（2026-09-19、コミット 31a5a1d の上で実施）
- **案内（`README.md` の英語・日本語の両節）**: 「使い方」を、2つの zip からの選択 → 丸ごと解凍（`Config.txt` と `Resources` を exe の隣に置いたまま）→ 起動（インストール不要）の順に改めた。「配布物の選び方」（英語は「Which Download to Choose」）の節を加え、ランタイムの要否・zip と exe の大きさ（3.3 の実測: 自己完結 zip 約59MB・exe 約140MB、軽量版 zip 約0.5MB・exe 約1MB）・選び方（迷ったら自己完結版）・軽量版に要るランタイム（.NET 10 Desktop Runtime (x64)、入手先 https://dotnet.microsoft.com/download/dotnet/10.0 ）を書いた。「システム要件」は OS を Windows 10 / 11（64ビット、x64）、ランタイムを形態ごとに書き分けた。各言語の Readme の場所を実際の `Resources/Readme/` に直した。`Resources/Readme/*.txt` には実行基盤の記述が無いため変えていない
- **配布物の中身**（`artifacts/package` の2つの zip の exe から単一ファイルの目録と埋め込みの deps.json を読んで確かめた。アプリは起動していない）
  - 両形態: `LargeFolderFinder.dll`、`MessagePack.dll`・`MessagePack.Annotations.dll`（3.1.9）、`Microsoft.NET.StringTools.dll`（17.11.4。MessagePack が推移的に引き込む）、`YamlDotNet.dll`（18.1.0）
  - 自己完結版だけ: ランタイムパック `Microsoft.NETCore.App.Runtime.win-x64` 10.0.12 と `Microsoft.WindowsDesktop.App.Runtime.win-x64` 10.0.12（目録387項目。WPF のネイティブ DLL 5本 `D3DCompiler_47_cor3`・`PenImc_cor3`・`PresentationNative_cor3`・`vcruntime140_cor3`・`wpfgfx_cor3` を含む）
  - テスト専用の部品（xunit.v3、Microsoft Testing Platform）と、取り除いた Ookii.Dialogs.Wpf・Fody・Costura.Fody は入っていない
- **著作権表示（`Resources/License/ThirdPartyNotices.txt`）**: Ookii.Dialogs.Wpf・Fody・Costura.Fody を削除。1〜3（両形態）に MessagePack、Microsoft.NET.StringTools、YamlDotNet を、4〜5（自己完結版だけ）に .NET ランタイムと Windows Desktop Runtime（WPF）を置き、冒頭に形態ごとの適用範囲を書いた。本文は出典の文面をそのまま連結した（改行だけ CRLF にそろえた。UTF-8・BOM なし。.NET ランタイムの第三者表示に ASCII 以外の文字があるため）。約92KB
  - MessagePack: 公式リポジトリの `LICENSE`（NuGet の版 3.1.9 のコミット aa16e71）。従来の記載に無かった `BufferWriter.cs`（Apache 2.0）の節が含まれる
  - Microsoft.NET.StringTools: dotnet/msbuild の `LICENSE`（パッケージのコミット 37eb419、MIT）と、パッケージ同梱の `notices/THIRDPARTYNOTICES.txt`
  - YamlDotNet: 公式リポジトリの `LICENSE.txt`（18.1.0 のコミット 748334a）。文面は従来と同じ
  - .NET ランタイム: NuGet キャッシュのランタイムパック 10.0.12 の `LICENSE.TXT` と `THIRD-PARTY-NOTICES.TXT`（dotnet/runtime の v10.0.12 と同一であることを確かめた）
  - Windows Desktop Runtime: ランタイムパック 10.0.12 の `LICENSE` と、dotnet/wpf の v10.0.12 の `THIRD-PARTY-NOTICES.TXT`（パッケージに第三者表示が無いため）
- **設計に無い判断**: 設計は .NET ランタイムについて「MIT を加える」とだけ指定するが、同梱するランタイムの第三者表示（zlib など、バイナリの配布で表示を求めるものを含む）も本文のまま載せた。表示の版はこの時点の配布物（10.0.12）の文面で、SDK の版を変えてランタイムの版が変わったら見直す
- **確かめたこと**: `README.md` に「4.8」「.NET Framework」「Ookii」「Costura」が0件。著作権表示に Ookii・Fody・Costura・xunit.v3・Microsoft.Testing が0件で、8つの出典の本文がすべて一字一句含まれる。アプリ単体のビルド（`dotnet build LargeFolderFinder.csproj -c Release`）は警告0・エラー0で、出力先の著作権表示が更新後のものと同一。軽量版をスクラッチの場所に発行し直し、発行物の著作権表示が同一であることを確かめた（`artifacts/publish` はプロセスが残ってロックされているため触れていない。`artifacts/package` の zip は古い著作権表示のままで、作り直しは 5.3・5.4 の発行で行われる）

### 5.2 steering の移行後の事実への更新（2026-09-19、コミット 65e2e71 の上で実施）
- **更新したもの**（事実の出典はこの「移行の記録」の 1.1〜5.1、設計段階の実測、tasks.md の Implementation Notes）
  - `tech.md`: 対象（`net10.0-windows`、`win-x64`、`UseWindowsForms` なし）、主要ライブラリ（MessagePack 3.1.9、YamlDotNet 18.1.0、CommunityToolkit.Mvvm は未導入で下限 8.4.2、xunit.v3 はテスト専用。外部のダイアログ部品と埋め込みの仕組みの行を削除し、`OpenFolderDialog` を使うことを記載）、テストの節（「自動テストは存在しません」を、テストプロジェクトの方式・スキップの扱い・期待値データの更新済みの事実に置き換え）、SDK の版と理由・版を変えるときの手順、コマンド（ビルド・テスト・検証ツールの新しい場所・発行・起動確認・梱包）、「発行と配布」「既知の制約」の節を新設。「.NET Framework 4.8 を維持」「Costura による単一 exe 化」の判断を、移行後の判断（2026-09-17 の利用者の決定）と「単一 exe の配布」「スクリプトを単一の入口にする」に置き換えた
  - `product.md`: 配布形態を2形態に、「ランタイム導入不要（.NET Framework 4.8 は Windows 標準搭載）」を自己完結版での事実に改め、優先順位の「導入の手軽さ」に自己完結版で守ることを添えた
  - `structure.md`: 検証ツールの出力先（RID なし）、`Tests/`、`build/`・`.github/workflows/`、`global.json`・`baselines/`、`artifacts/` と `DefaultItemExcludes` の追加分
  - `decisions.md`: 既存の「.NET 9 の自己完結型・単一ファイルの発行設定」の項目の末尾に2行を追記（既存の記述は変えていない）。197 → 199 行
- **設計に無い判断**
  - `tech.md` のアプリデータの場所を `…\Large Folder Finder\` から `…\LargeFolderFinder\` に直した。移行前からの記述の誤りで、`AppConstants.AppDataDirectory`（`AppInfo.Organization` と `AppInfo.Title`）と 3.2 の実際のフォルダに合わせた
  - NuGet の脆弱性の警告で CI が失敗しうることは、SDK 10.0.401 の `NuGet.targets` で確かめた（`NuGetAudit` は既定で有効、`NuGetAuditLevel` は `low`、対象が `net10.0` 以上なら `NuGetAuditMode` は `all` で推移的な依存も監査する）。警告の番号 `NU1901`〜`NU1904` は NuGet の監査の警告の番号
  - `performance.md` の「自動テストが無いため、速度に影響する変更は必ず実測で確認」は境界の外のため変えていない（速度の実測が要ることは移行後も同じ）。`roadmap.md` の経緯の記述も変えていない
- **steering に書いたコマンドの確認**（手元、SDK 10.0.401、利用統計の送信を止める環境変数を設定、順番に実行。アプリは起動していない）

| コマンド | 結果 | 終了コード |
|---|---|---|
| `dotnet --version` | `10.0.401` | 0 |
| `dotnet build LargeFolderFinder.sln -c Release -warnaserror` | 警告 0、エラー 0 | 0 |
| `dotnet test --solution LargeFolderFinder.sln -c Release --no-build` | 合計 8、失敗 0、成功 8、スキップ 0 | 0 |
| `Tools/GoldenBaseline/bin/Release/net10.0-windows/GoldenBaseline.exe compare --golden baselines/fixture-v1.golden.txt` | 判定: 一致 | 0 |
| `Tools/GoldenBaseline/bin/Release/net10.0-windows/GoldenBaseline.exe selfcheck` | 128 件中 0 件が失敗 | 0 |
| `Tools/LocalizationCheck/bin/Release/net10.0-windows/LocalizationCheck.exe check` | 問題はありません（言語 13、キー 81） | 0 |
| `Tools/LocalizationCheck/bin/Release/net10.0-windows/LocalizationCheck.exe selfcheck` | 59 件中 0 件が失敗 | 0 |
| `build/Publish.ps1 -Form FrameworkDependent -OutputDir <スクラッチ>` | exe 1,052,929 バイト | 0 |
| `build/Publish.ps1 -Form SelfContained -OutputDir <スクラッチ>` | exe 140,586,265 バイト | 0 |
| `build/Package.ps1`（上の2つの発行先と出力先をスクラッチに指定） | `LargeFolderFinder.zip` 59,210,861 バイト、`LargeFolderFinder-FrameworkDependent.zip` 505,568 バイト | 0 |

  - 発行は `-OutputDir` にスクラッチの空のフォルダを渡した。`artifacts/publish/SelfContained` は 4.3 で残ったプロセスが exe をロックしているため触れていない。`build/Test-Launch.ps1` はアプリを起動するため実行していない（3.2・4.3 で確認済み）
  - zip は 3.3 より両方とも約13.4KB（13,425・13,424 バイト）大きい。exe の大きさは同じなので、5.1 で著作権表示が約92KB に増えた分とみられる

### 5.3 自動ビルドの取り止め（2026-09-21、コミット 33b1c52 の上で実施）

**経緯**

タスク5.3 のために `refactor/modernization` を GitHub へ push したところ、拒否された。

```
! [remote rejected] refactor/modernization -> refactor/modernization
  (refusing to allow an OAuth App to create or update workflow
   `.github/workflows/ci.yml` without `workflow` scope)
```

保存されている認証情報に `workflow` スコープが無く、`.github/workflows/` を含むコミットを送れないためである。これを機に利用者から「GitHub Actions を使う意味が見えていない」という疑問が出たため、費用対効果を評価し直した。

**評価**

- ワークフローは `build/Publish.ps1` → `Test-Launch.ps1` → `Package.ps1` を呼ぶだけの薄い層であり、**発行・起動確認・梱包は Actions が無くても手元で同じことができる**
- Actions が追加で提供するのは「クリーンな環境でのビルドとテストの確認」と「下書きのリリースの自動作成」の2点のみ
- GUI アプリの起動確認がホスト型ランナーで成立するかは不明で、タスク5.3 の定義自体が代替手段への切り替えを織り込んでいた（設計時点で弱いと認識されていた）
- 本プロジェクトは単独開発でリリース頻度が低く、ロードマップが掲げる4つの目的（可読性・技術的追従・速度・UI）のいずれにも Actions は直接寄与しない

**決定**

**GitHub Actions は使わない。** `.github/workflows/ci.yml` と `release.yml` を削除した。

- ビルドとテストは手元で `dotnet build LargeFolderFinder.sln -c Release -warnaserror` と `dotnet test --solution LargeFolderFinder.sln -c Release --no-build` を実行する
- リリースは `build/` の3スクリプトを順に実行して2つの zip を作り、GitHub の画面でリリースを作って添付する
- 4.2・4.3 の成果物は削除したが、タスク自体は当時の完了条件を満たしていたため `[x]` のまま残し、削除した旨を追記した

**本来 5.3 で確かめるはずだった事項の扱い**

| 事項 | 扱い |
|---|---|
| クリーンな環境でのビルドとテスト | 行わない。手元での実行で代替する |
| 下書きのリリースの zip の中身の照合 | 5.4 で手元の zip を直接確かめる |
| CI のログでスキップが0件であること | 手元での実測に置き換える。2026-09-21 の実行で8件すべて成功・スキップ0件を確認済み |
| ランナーでの GUI 起動確認の成否 | 対象外。起動確認は手元で行う |

**注意**

ワークフローのファイルは過去のコミットの履歴に残るため、**このブランチを push するには `workflow` スコープを持つ認証が必要**である（削除するコミットを足しても、履歴に作成したコミットがある限り拒否される）。履歴の書き換えは96コミットに対して割に合わないと判断し、認証側を直す方針とした。

### 5.4 移行全体の確認と利用者の確認手順（2026-09-22、コミット 5158948 の上で実施）

**前提**: 2026-09-19 05:03 に起動確認で起動した自己完結版（PID 58936）が終了せず、`artifacts/publish/SelfContained/` を掴んだまま動き続けていた（3.2 の記録の事象と同じ）。利用者に PC を再起動してもらい（2026-09-22 00:55 起動）、残留プロセスが無いことを確かめてから始めた。

**手元での一通りの確認**（すべて成功）

| 確認 | 結果 |
|---|---|
| `dotnet build LargeFolderFinder.sln -c Release -warnaserror` | 4プロジェクト成功、警告0件・エラー0件 |
| `dotnet test --solution LargeFolderFinder.sln -c Release --no-build` | 8件すべて成功、スキップ0件 |
| `build/Publish.ps1`（自己完結・軽量の2形態） | どちらも終了コード0 |
| `build/Test-Launch.ps1`（2形態） | どちらも「閉じる要求で終了（終了コード0）」、残留プロセスなし |
| `build/Package.ps1` | `LargeFolderFinder.zip`（59,210,867 バイト）と `LargeFolderFinder-FrameworkDependent.zip`（505,571 バイト） |

- テストのコマンドは `--solution` を付ける。付けずに `dotnet test LargeFolderFinder.sln` とすると、Microsoft Testing Platform の方式では0件実行・終了コード5になる（テストの不具合ではない）

**変更範囲の確認**（移行前 `48d8a5d` から HEAD までの差分）: `Services/Scanner.cs`（走査の処理）、`Helpers/Win32.cs`（P/Invoke）、`Services/ResultFormatter.cs`、`Services/TreeFilter.cs`、すべての XAML（画面の構成）、`Resources/Languages/*.yaml`（訳文）は**無変更**。アプリ本体で変わったのは `Helpers/RelayCommand.cs`（2.2 の警告の解消）と `Views/MainWindow.xaml.cs`（2.3 のフォルダ選択の置き換え）のみ。

**保存データの互換性（要件4.4）**: 移行前の版は `artifacts/legacy-net48/`（ProductVersion `1.0.3+48d8a5d…`）を使った。全セッションの読み込み完了をログで待ってから閉じる要求で終了させ、`Settings loaded (Result: Success)`・`Loaded … successfully` の件数・エラー行の有無を見た。

| 方向 | 読ませたデータ | 結果 |
|---|---|---|
| 移行後 ← 移行前 | 移行前の版が書いた設定とセッション8件（退避の状態） | 設定の読み込み成功、セッション 8/8、エラー行0、終了コード0 |
| 移行前 ← 移行後 | 移行後の版が閉じる時に保存し直した設定 | 設定の読み込み成功、セッション 8/8、エラー行0、終了コード0 |
| 移行前 ← 移行後 | **移行後の版が中身ごと書き直したセッション**（`Scan20260915_1715`、36,933,915 → 36,929,965 バイト。3.2 の記録で「必ず見る」とした点） | 当該セッションを含め 8/8 を読み込み成功、エラー行0 |

- 移行後の版は、言語ファイル13件を発行先の `Resources/Languages` から読み、画面への適用に成功した（ログで確認）
- `Config.txt` の読み込みはアプリがログに出さない（`Config.Load` は失敗しても黙って既定値を返す）。代わりに、起動後も発行先の `Config.txt` が作り直されていない（更新時刻が元のまま、中身がリポジトリと同一）ことから、単一ファイルの発行でも exe の隣の `Config.txt` を正しく見つけたことを確かめた。**解析まで成功したかはログからは分からない**（`scan-correctness` の例外の握りつぶしの是正で扱う）

**アプリデータの扱いと、手順の誤りの記録**

- **手元の起動確認を、アプリデータを退避せずに実行してしまった**（3.2 の記録の手順に反する）。起動確認とテストにより、`Settings.msgpack`（469 → 467 バイト）とセッション1件の中身が書き換わり、ログが入れ替わった
- 3.2〜4.3 の確認時に作った退避（2026-09-19 時点、18ファイル）が作業領域に残っており、今日変わっていないセッション4件と `Cache.txt` がバイト単位で一致したため、今日の作業直前の状態を表すと判断した
- 互換性の確認はこの退避を起点に行い、最後に `robocopy /MIR` で退避の状態に戻して、全ファイル（18本）の相対パス・大きさ・SHA-256 の一覧が**退避と完全に一致**することを確かめた。組織のフォルダの下に別のフォルダは増えていない
- **テストの実行も利用者のアプリデータにログを書く**ことが分かった。`GoldenBaseline` が本体の `Scanner` を呼び、その中の `Logger` がアプリデータの `Logs/` に書くため。今回テストで3本増えた。以後、手元でテストを走らせる前にもアプリデータを退避する

## 利用者による確認の手順

移行後の版は `artifacts/package/` の2つの zip（または `artifacts/publish/` の2つのフォルダ）にある。**確認の前にアプリデータ（`%LOCALAPPDATA%\Cat & Chocolate Laboratory\LargeFolderFinder`）をフォルダごと別の場所に複製しておくこと。**

2形態（自己完結版 `LargeFolderFinder.zip`、軽量版 `LargeFolderFinder-FrameworkDependent.zip`）それぞれで、次を確かめる。

1. **起動と以前の状態の引き継ぎ**: 起動すると、以前使っていた言語・レイアウト（縦／横）・フォントサイズ・ウィンドウの位置と大きさ、および以前開いていたタブ（最大8つ）がそのまま出ること
2. **フォルダの選択（新しいダイアログ）**: 参照ボタンで Windows 標準の「フォルダーの選択」が開くこと。確かめる点は次の3つ
   - 説明の文言（ダイアログのタイトル）が移行前と同じ意味で表示されること
   - **入力欄のフォルダの「中」から開くこと**（移行前は親フォルダを開いて対象を選んだ状態だった可能性がある。どちらが使いやすいかの感想もほしい）
   - キャンセルすると入力欄が変わらないこと。入力欄に使えない文字を含むパスがあるときは、エラーではなく既定の場所から開くこと
3. **走査と表示**: 走査、タブ、並べ替え、絞り込み（ワイルドカード／正規表現）、折りたたみ、コピー、表示単位の切り替え、言語の切り替えが移行前と同じように動くこと
4. **付属の表示**: メニューから設定ファイル・Readme・ライセンス（アプリ／第三者）を開けること
5. **管理者として開き直す**: メニューの「管理者として開き直す」で UAC の確認が出て、管理者として起動し直すこと
6. **（可能なら）ランタイムの無い PC**: .NET 10 の入っていない PC で自己完結版が起動すること。軽量版はランタイムの導入を求める案内が出ること

問題があれば、画面の状態と `%LOCALAPPDATA%\Cat & Chocolate Laboratory\LargeFolderFinder\Logs` の最新のログを添えて知らせる。確認が終わったら、複製しておいたアプリデータで元に戻してよい（移行前後の版はどちらも相手の書いたデータを読めることを確かめてある）。

### フィーチャーの最終判定（2026-09-22、/kiro-validate-impl）
- **判定: GO**
- テスト8件成功・スキップ0件、2形態の起動確認は成功・残留プロセスなし、移行で追加した行に残置の印と秘密情報なし（いずれもアプリデータを退避して実施し、復元後にハッシュ一覧の一致を確認）
- 要件の網羅 47/47。Actions の取り止めに合わせて、requirements.md の Requirement 5（5.1〜5.6、5.8）・6.3・7.3 を手元の手順を基準にした記述に改め、design.md からワークフローの部品・図・ファイル・試験の記述を除いた
- 境界の監査: 走査の処理・P/Invoke・画面の構成・訳文は移行前から無変更。出力先のパスは検証ツール・テスト・スクリプト・steering で一致
- 後続に引き継ぐ事項: 事前カウントの長いパスの制約と `Config.txt` の解析の失敗の不可視（`scan-correctness`）、終了済みのまま残るプロセスの事象（原因未特定）、TEMP が長いと比較がスキップされる検証の弱点（手元の実行で「スキップ0件」を確かめる）

