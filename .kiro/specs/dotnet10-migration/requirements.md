# Requirements Document

## Project Description (Input)
Large Folder Finder を .NET Framework 4.8 から .NET 10 へ移行し、依存を整理し、配布物の生成を自動化する。

**誰の問題か**: 開発者（個人開発、単独）と、本アプリの利用者。

**現状**: 対象は `net48`。配布は Costura により依存 DLL を exe に埋め込んだ単一ファイルで、GitHub Releases に zip を手作業で登録している。CI はない。`.NET Framework` 上では、260文字を超えるパスがファイルの列挙から無言で脱落する欠陥を直せない。テストの基盤もない。依存のうち Ookii.Dialogs.Wpf は実質メンテナンスが止まり、MessagePack 3.1.7 は脆弱性の修正版ちょうどで余裕がない。

**何が変わるべきか**: .NET 10（LTS、サポート終了 2028-11-14）でビルド・動作し、依存が整理され、2形態の配布物がタグから自動で作られ、テスト基盤の上で走査結果の同等性を機械的に確かめられる。

**制約**: 機能と UI は変えない。長いパス対応の実装、速度の最適化、MVVM 基盤の入れ替え、C# の新しい言語機能の全面適用は後続スペックが担う。詳細は [brief.md](brief.md) を参照。

### 前提と決定事項

- **配布の形**（2026-09-17 利用者の決定）: 自己完結の単一 exe を既定とし、フレームワーク依存版もあわせて配る。過去に .NET 9 から .NET Framework 4.8 へ戻した理由は「利用者にランタイムの導入を求めない」ことと「実行ファイルの軽さ」の2つだった（`.kiro/steering/decisions.md`）。自己完結版は1つ目を守るが、WPF がトリミングに非対応（[dotnet/wpf#3811](https://github.com/dotnet/wpf/issues/3811) 未解決）のため2つ目は失われる。軽さを求める利用者にはフレームワーク依存版を用意し、README で違いを案内する
- **走査結果の変化**（2026-09-17 利用者の決定）: 移行だけで、既知の不具合による欠落4件（260文字を超える階層のフォルダ2件とファイル2件）が結果に現れる見込みである。.NET (Core) 以降のファイル列挙は長いパスを扱えるため。差がこの4件とそれを含む親フォルダのサイズだけであることを確かめたうえで、本スペックで期待値データを更新する。それ以外の差は移行の不具合として直す
- **リリース**（2026-09-17 利用者の決定）: バージョンのタグを push すると、2形態のビルド・起動確認・zip の作成・zip を添えた**下書きの**リリースの作成までを自動で行う。公開の操作は人が行う
- **SDK の版は実測で決める**（2026-09-17 調査）: WPF と単一ファイル発行の組み合わせに、SDK 10.0.200 系・202 系で起動時の例外や無言終了の報告がある（[dotnet/wpf#11678](https://github.com/dotnet/wpf/issues/11678) 未解決、10.0.103 と 10.0.106 では出ない）。また `--self-contained false` が効かない不具合も未解決である（[dotnet/sdk#51888](https://github.com/dotnet/sdk/issues/51888)。`--no-self-contained` を使えば回避できる）。最新は SDK 10.0.401 と 10.0.112（ランタイム 10.0.12、2026-09-08）
- **アプリの P/Invoke は移行後も MAX_PATH の制約を受ける**: フォルダ数を数える処理は Windows API を直接呼ぶため、ファイルの列挙と数え方がずれる。この解消は `scan-correctness` の範囲とする
- **自動ビルドの取り止め**（2026-09-21 利用者の決定）: GitHub Actions による自動ビルド（タグの push からの2形態のビルド・起動確認・下書きリリースの作成）は取り止め、`.github/workflows/ci.yml` と `release.yml` を削除した。配布物の生成とリリースは、開発者が手元で発行・梱包・起動確認のスクリプトを実行し、GitHub の画面でリリースを作る手順に一本化する。以下の Requirement 5・6.3・7.3 は、この決定に合わせて手元の手順を基準にした基準に改めてある。経緯と評価は research.md の「5.3 自動ビルドの取り止め」を参照

## Introduction

本フィーチャーは、Large Folder Finder の実行基盤を .NET 10 へ移し、後続のすべてのスペックが前提とする土台（長いパスを扱える基盤、テストの基盤、手元で完結する配布物の生成手順）を整えるものである。

利用者から見た機能と操作は変えない。変わるのは、配布物の形（自己完結版とフレームワーク依存版の2つ）と、260文字を超える階層が走査結果に現れるようになることだけである。

## Boundary Context

- **In scope**: 対象フレームワークの変更と、それに伴うコードとプロジェクト設定の修正、Costura.Fody と Fody の除去と単一ファイル発行への置き換え、Ookii.Dialogs.Wpf の削除と標準のフォルダ選択ダイアログへの置き換え、依存の更新とバージョン下限の確定、SDK の固定、2形態の配布物を発行・梱包・起動確認する手元のスクリプト、テストプロジェクトの新設と期待値データとの突き合わせ、検証ツール（走査結果の検証、翻訳の網羅の検証）の移行への追従、`ThirdPartyNotices.txt` と README の更新、走査結果の差の確認と期待値データの更新
- **Out of scope**: 長いパス対応の実装（`scan-correctness`。移行に伴って結果が変わる分は本スペックで記録・更新する）、速度の最適化（`scan-performance`）、MVVM 基盤の入れ替えとコードの構造変更（`architecture-refactoring`）、C# の新しい言語機能の全面適用、UI の見た目と操作の変更、保存データの形式の変更、失われた機能の復元（`architecture-refactoring` の例外）
- **Adjacent expectations**: `scan-golden-baseline` の期待値データと比較ツールを、移行の影響を測る基準として使う。`localization-completeness` が加える翻訳の網羅の検証ツールも、移行後に動き続ける必要がある。後続のすべてのスペックは、本スペックが整えるテスト基盤の上で作業する

## Requirements

### Requirement 1: 実行基盤の移行

**Objective:** As a 開発者, I want アプリが .NET 10 でビルドでき、これまでどおり動くこと, so that 以後の修正と最適化を現行の基盤の上で行える

#### Acceptance Criteria

1. The Large Folder Finder shall .NET 10 の Windows 向け対象フレームワークでビルドでき、ビルドの警告を移行前より増やさない
2. When 利用者がアプリを起動したとき, the Large Folder Finder shall 移行前と同じ画面を表示し、フォルダの選択、走査、タブの操作、結果の表示と並べ替え、絞り込み、クリップボードへのコピー、表示言語の切り替え、設定ファイルと Readme とライセンスの表示が移行前と同じように行える
3. The Large Folder Finder shall アプリの設定、セッション、ログの保存場所（利用者ごとのローカルのアプリデータの配下）を移行前と同じ場所に保つ
4. The Large Folder Finder shall 移行にあたって、`.NET Framework` 固有の API と、`.NET 10` で提供されない API の使用をなくす
5. The Large Folder Finder shall 移行によって画面の表示内容、操作、既定値を変えない
6. If 移行の過程で使われていないコードや設定（未使用の宣言、旧式の参照、不要な UI 基盤の指定）が見つかったとき, then the Large Folder Finder shall 移行に必要な範囲でそれらを取り除き、取り除いたものを記録する

### Requirement 2: 配布物の2形態

**Objective:** As a 利用者, I want ランタイムの導入なしで使える配布物と、軽い配布物のどちらかを選べること, so that 自分の環境と好みに合う方を使える

#### Acceptance Criteria

1. The Large Folder Finder shall ランタイムの別途導入を必要としない自己完結の単一実行ファイルを、既定の配布物として生成する
2. The Large Folder Finder shall ランタイムの導入を前提とする軽量な配布物を、もう1つの選択肢として生成する
3. While 自己完結の配布物を使っている, the Large Folder Finder shall 実行ファイルの隣に置かれた設定ファイル、言語ファイル、Readme、ライセンスを移行前と同じように読み取る
4. While 軽量な配布物を使っている, the Large Folder Finder shall 同じ設定ファイルと資料を同じように読み取る
5. When 利用者が「管理者として開き直す」を選んだとき, the Large Folder Finder shall どちらの配布物でもアプリ自身を管理者権限で起動し直す
6. When 利用者がバージョン情報を表示したとき, the Large Folder Finder shall 配布物の版を表す文字列を、移行前と同じ形式（余分な付加情報のない形）で表示する
7. The Large Folder Finder shall 配布物の中身（実行ファイルと、同梱する設定ファイル・言語ファイル・Readme・ライセンス）の構成を、両方の配布物で同じにする
8. The Large Folder Finder shall 2つの配布物の違い（ランタイムの要否、大きさ）と選び方を、利用者向けの案内に記載する

### Requirement 3: 依存の整理

**Objective:** As a 開発者, I want 依存が最新かつ手入れの続いているものだけになること, so that 脆弱性と保守の停止に振り回されない

#### Acceptance Criteria

1. The Large Folder Finder shall 依存 DLL の埋め込みに使っていた仕組み（Costura.Fody と Fody）を取り除き、実行基盤が備える単一ファイルの発行で置き換える
2. When 利用者が走査するフォルダを選ぶとき, the Large Folder Finder shall 外部のダイアログ部品に依存せず、実行基盤が備えるフォルダ選択ダイアログを表示する
3. The Large Folder Finder shall フォルダ選択ダイアログで、移行前と同じく説明の文言の表示と、現在入力されているパスからの開始を行う
4. The Large Folder Finder shall 保存データの読み書きに使う部品を、既知の脆弱性の修正を含む版以上に更新する
5. The Large Folder Finder shall 設定と言語ファイルの読み込みに使う部品を更新し、更新に伴う挙動の変化がないことを確かめる
6. The Large Folder Finder shall 後続スペックで導入する MVVM の部品について、.NET 10 でビルドできる版の下限を確定して記録する
7. When 依存を追加または削除したとき, the Large Folder Finder shall 配布物に含まれる第三者の著作権表示の一覧を実態に合わせて更新する

### Requirement 4: 走査結果の同等性

**Objective:** As a 開発者, I want 移行で集計値が変わっていないことを確かめられること, so that 後続の最適化の基準を保てる

#### Acceptance Criteria

1. When 移行後に期待値データと走査結果を比較したとき, the 開発者 shall 差が「260文字を超える階層の既知の欠落4件」と「それらを含む親フォルダのサイズ」だけであることを確かめる
2. If 上記以外の差が見つかったとき, then the Large Folder Finder shall その差を移行に伴う不具合として扱い、原因を取り除く
3. When 差が既知の欠落に由来するものだけであることを確かめたとき, the 開発者 shall 期待値データを移行後の結果で更新し、更新の理由と差の内訳を記録に残す
4. The Large Folder Finder shall 移行の前後で、保存済みのセッションの読み書き、設定の保存と読み込み、ログの出力が引き続き行えることを確かめる
5. The Large Folder Finder shall 長いパスを扱えるようにするための実装（走査の呼び出し方の変更、フォルダ数を数える処理の置き換え）を本スペックでは行わない

### Requirement 5: 配布物の生成

**Objective:** As a 開発者, I want 手元の決まった手順で2形態の配布物を用意できること, so that リリースの手間が移行前より増えない

#### Acceptance Criteria

1. When 開発者が配布物を作る手順を実行したとき, the 発行と梱包の手順 shall 2形態の配布物をビルドし、それぞれを配布用の書庫にまとめる
2. When 開発者が起動確認の手順を実行したとき, the 起動確認の手順 shall それぞれの実行ファイルが実際に起動して使える状態になることを確かめ、起動に失敗したときは失敗として報告する
3. When ビルドと起動の確認が成功したとき, the 開発者 shall GitHub の画面でリリースを作り、2つの書庫を添える
4. The Large Folder Finder shall リリースの公開そのものを自動では行わず、開発者の操作に委ねる
5. When 開発者が変更を送ろうとしたとき, the 開発者 shall 送る前に手元でビルドとテストの手順を実行し、失敗を見逃さない
6. The Large Folder Finder shall ビルドに使う開発キットの版を固定し、固定した版を記録に残す
7. If 固定した版で配布物の起動に問題が見つかったとき, then the 開発者 shall 版を変えて確かめ、選んだ版とその理由を記録する
8. The Large Folder Finder shall 発行・梱包・起動確認の手順をスクリプトとして提供し、開発者が何度でも同じ手順で配布物を作れるようにする

### Requirement 6: テストの基盤

**Objective:** As a 開発者, I want 自動テストを実行できる土台があること, so that 以後の大きな変更を機械的に検証できる

#### Acceptance Criteria

1. The 開発者 shall 移行後のコードに対して、テストの実行を1つのコマンドで行える
2. When テストを実行したとき, the テスト shall 期待値データと走査結果の突き合わせを行い、その結果を成否として示す
3. When 開発者がビルドの手順を実行したとき, the ビルドの手順 shall 続けてテストを実行し、失敗を見逃さない
4. If 期待値データの検証が実行環境の制約（権限、パスの長さ）で成立しないとき, then the テスト shall その事実を、成功と区別できる形で報告する
5. The テスト shall 既存の検証ツールが持つ判定の仕組みを作り直さず、利用できる形で取り込む

### Requirement 7: 検証ツールの追従

**Objective:** As a 開発者, I want 既存の検証ツールが移行後も使えること, so that 走査結果と翻訳の網羅の確認を続けられる

#### Acceptance Criteria

1. The 走査結果の検証ツール shall 移行後のアプリを対象に、これまでと同じコマンドと終了コードで動作する
2. The 走査結果の検証ツール shall 実行基盤の変更によって使えなくなる API（アクセス権の設定など）を、.NET 10 で提供される手段に置き換える
3. The 翻訳の網羅の検証ツール shall 移行後も、これまでと同じコマンドと終了コードで動作し、テストの中で実行されて、問題があるときはテストの失敗として扱わせる
4. When 検証ツールを移行したとき, the 開発者 shall 各ツールの自己検証がすべて成功することを確かめる
5. The 開発者 shall 検証ツールの判定規則（期待値の形式、翻訳の網羅の判定）を移行に伴って変えない

### Requirement 8: 記録の更新

**Objective:** As a 開発者, I want 移行後の事実が記録に反映されていること, so that 後続の作業が古い前提で進まない

#### Acceptance Criteria

1. When 移行が完了したとき, the 開発者 shall 実行基盤、依存、配布方法、ビルドと実行のコマンドに関する記録を移行後の事実に更新する
2. The 開発者 shall 利用者向けの案内（動作環境、入手と起動の手順）を移行後の事実に更新する
3. The 開発者 shall 移行に伴って生じた既知の制約（配布物の大きさ、フォルダ数を数える処理に残る長さの制限、固定した開発キットの版とその理由）を記録に残す
