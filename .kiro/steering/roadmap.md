# Roadmap

## Overview

Large Folder Finder v1.0.3 は、2025年11月〜2026年1月ごろの旧世代 AI モデル（Gemini 2.0 Flash / 2.5 Flash / 2.5 Pro）を用いて実装された。動作はしているが、可読性・技術的な追従・最適化・UI の各面に課題が蓄積している。本ロードマップは、**既存の要望と仕様を維持したまま**、コードベースを現代的な水準へ引き上げるための作業計画である。

中心となる技術判断は **.NET Framework 4.8 から .NET 10（LTS）への移行**である。この移行は単なる追従ではなく、検出済みの重大バグ（260文字超のパスが無言で集計から漏れる）を構造的に解消し、手書き P/Invoke を BCL の `FileSystemEnumerator<T>` に置き換えて速度と安全性を同時に改善する土台となる。

## Approach Decision

- **Chosen**: ゴールデン「データ」先行のハイブリッド方式
  1. 現行版で既知フォルダを走査し、**パス→サイズの一覧をテキストで固定**して期待値とする（テストコードではなくデータを先に作る）
  2. .NET 10 へ移行する
  3. 移行後に .NET 10 上でテストプロジェクトを構築し、そのゴールデンデータと突き合わせる

- **Why**:
  - 保存データの互換性を捨てる判断をしたため、シリアライズ形式やクラス構造は自由に変えてよい。一方で**走査結果の数値（どのフォルダが何バイトか）だけは変わってはならない**。守るべき本質はデータであり、データで守るのが最も移行に強い
  - ゴールデンデータは形式非依存なので、.NET Framework から .NET 10 への移行をまたいで使い続けられる
  - .NET Framework 上でテストコードや長いパス対応を書くと、移行時に捨てる二重作業になる

- **Rejected alternatives**:
  - **A. 安全網を完全に先行**（現行環境でテストプロジェクトを整備してから移行）: 常に回帰検出できる点は優れるが、テストコードと長いパス対応が二重作業になり総期間が最も長い
  - **B. .NET 10 移行を最優先**（土台を先に入れ替える）: 二重作業は最小だが、最大の変更を安全網ゼロで行うことになり、移行で集計値が変わっても検出できない

## Scope

- **In**:
  - .NET 10（LTS）への移行と、それに伴う依存関係の整理・更新
  - 検出済みの実バグ修正（長いパス、走査中の競合、翻訳キー欠落、デバッグ残骸）
  - 走査速度の最適化
  - アーキテクチャの整理（コードビハインドの肥大解消、MVVM の徹底）
  - UI の視認性改善とデザイントークンの整備
  - ゴールデンデータによる安全網
  - 旧 AI が生成した設計ドキュメントからの要望・仕様の抽出と保全

- **Out**:
  - 新機能の追加（本プロジェクトは既存機能の維持が前提）
  - 保存データの後方互換（**互換性は捨てる方針で決定済み**。v2.0.0 として扱い、既存利用者には履歴がリセットされる旨を告知する）
  - Windows 以外のプラットフォーム対応
  - UI コンポーネントライブラリの全面採用（自前のデザイントークン整備を優先する）

## Constraints

### プロダクト方針（product.md の優先順位に従う）
1. スキャン速度 — ここを犠牲にする変更は原則採用しない
2. 再スキャンを強いないこと
3. 導入の手軽さ
4. 機能の豊富さ

### 速度の目標（2026-09-22 利用者の決定）
- **このアプリの存在意義のため、WizTree と同等以上の走査速度を目標とする**
- WizTree の速さは、ローカルの NTFS ドライブを管理者として走査するときに MFT を直接読むことによる。NAS・NTFS 以外・管理者でない場合は通常の列挙に戻り、差は大きく縮まる（[diskanalyzer.com](https://diskanalyzer.com/)、[FolderSizes の比較](https://www.foldersizes.com/features/wiztree)）
- したがって目標は2つの場面に分けて持つ
  - **通常の列挙（NAS、管理者でない・NTFS 以外のローカル）**: WizTree と同等以上。このアプリの本来の主戦場で、並列化の作り込みで上回る余地がある（`scan-performance`）
  - **ローカルの NTFS を管理者として走査**: WizTree の最速モードと同等以上。MFT の直接の読み取りが無ければ届かない（`ntfs-mft-scan`）
- 比較は利用者の PC に WizTree を入れて同じ対象・同じ条件で測る（同梱しない）。手順と記録は performance.md に置く

### ライセンス
- **すべての依存は無料かつ商用利用可能であること。** GPL / LGPL の混入を認めない
- 検証済み: MessagePack / YamlDotNet / CommunityToolkit.Mvvm / Fody = MIT、xUnit v3 = Apache-2.0、Ookii.Dialogs.Wpf = BSD-3-Clause
- BSD-3-Clause と Apache-2.0 は著作権表示の同梱義務があるため `Resources/License/ThirdPartyNotices.txt` への記載が必要

### 技術的制約（実現性検証で確定した事項、2026年9月時点）
- **.NET 10 は LTS、サポート終了は 2028年11月14日。** .NET 8 / 9 はいずれも 2026年11月10日に終了するため移行先にならない
- **`--self-contained false` は .NET 10 SDK で機能しない**（[dotnet/sdk#51888](https://github.com/dotnet/sdk/issues/51888) オープン中）。発行では必ず `--no-self-contained` を使う
- **WPF + PublishSingleFile に SDK 10.0.200 / 10.0.202 のリグレッションあり**（[dotnet/wpf#11678](https://github.com/dotnet/wpf/issues/11678) 未トリアージ）。`global.json` で SDK を固定し、生成した exe の起動確認を発行の手順に組み込む
- **apphost は long path aware ではない**（[dotnet/runtime#43555](https://github.com/dotnet/runtime/issues/43555)）。BCL 経由なら 260 文字制限を受けないが、**手書き P/Invoke が1つでも残ると制限を受ける**。したがって長いパス対応には **P/Invoke の全廃が必須条件**
- **日本語を含む260文字超のパスで失敗する未解決の報告あり**（[dotnet/runtime#126535](https://github.com/dotnet/runtime/issues/126535)）。日本語パスを扱うツールのため実機検証が必須
- WPF はトリミング非対応（[dotnet/wpf#3811](https://github.com/dotnet/wpf/issues/3811) オープン中）。自己完結版のサイズ削減は期待できない
- 依存バージョンの下限: **MessagePack 3.1.8 以上**（3.1.7 未満に High を含む脆弱性10件）、**CommunityToolkit.Mvvm 8.4.2**（8.4.0 は .NET 10 でビルド不能）

### 配布形態
- **自己完結・単一exe 版（既定）とフレームワーク依存版（軽量）の2種類を併置**する
- 発行・起動確認・梱包は `build/` のスクリプトで行い、リリース作業を増やさない（2026-09-21 の決定で自動ビルドは使わない）

### 資料の保全
- 旧 AI が生成した設計ドキュメントは `docs/` 配下に42フォルダ存在するが、**`.gitignore` の `/docs/` により Git 管理外**である
- 方針として `docs/` は Git 管理外のまま、**要点を `.kiro/steering/` へ抽出**する
- **抽出が完了するまで、`docs/` は要望の記録の唯一の写しである。** この期間の消失リスクを認識しておくこと

## Boundary Strategy

- **Why this split**:
  - **安全網・資料保全・翻訳補完は他の作業に依存しない**ため、最初の波で並行して片付ける。特にゴールデンデータは以降すべての作業の前提になる
  - **.NET 10 移行を単独のスペックとして早期に置く**ことで、以降の作業が新環境の上で一度だけ行われるようにする。移行を後回しにすると、長いパス対応・テストコード・言語機能の利用が二重作業になる
  - **走査系（正しさ → 速度）とアーキテクチャ系（構造 → 見た目）を分離**する。前者は `Services/Scanner.cs` と `Models/FolderInfo.cs`、後者は `Views/` と `ViewModels/` が主戦場であり、責務の境界が明確
  - 正しさを速度より先に置くのは、**壊れたまま速くしても意味がない**ため。特に長いパスの欠落を残したまま最適化すると、ゴールデンデータ自体が不完全な期待値になる

- **Shared seams to watch**:
  - **`Models/FolderInfo.cs`** — `scan-performance` がノード構造を作り替え、`architecture-refactoring` が `FolderRowItem` の移設を行う。両者が同じ型に触れるため、performance を先に完了させる
  - **`Views/MainWindow.xaml.cs`（約1,260行）** — `scan-correctness`（競合修正）、`architecture-refactoring`（分割）、`ui-redesign`（表示）がいずれも触れる。直列化して競合を避ける
  - **`Services/LocalizationManager.cs` と `Resources/Languages/*.yaml`** — `localization-completeness` が整備した網羅チェックを、`ui-redesign` でラベルを追加する際に必ず通すこと
  - **ゴールデンデータの形式** — `scan-golden-baseline` が定義した形式に、`scan-correctness` と `scan-performance` が依存する。移行をまたぐため、.NET のバージョンやシリアライズ形式に依存しない素朴なテキスト形式にすること

## Specs (dependency order)

- [x] scan-golden-baseline -- 現行版の走査結果をパス→サイズの一覧として固定し、変更前後を比較する仕組みを整える。移行をまたぐ安全網。Dependencies: none
- [x] requirements-preservation -- `docs/` 配下42フォルダの設計ドキュメントから要望・仕様・設計判断の要点を抽出し、`.kiro/steering/` へ保全する。Dependencies: none
- [x] localization-completeness -- 11言語で欠落している翻訳キーを補完し、`LanguageKey` と全13言語の YAML の網羅を機械的に検証する仕組みを設ける。Dependencies: none
- [x] dotnet10-migration -- .NET 10 への移行、Costura.Fody の除去と PublishSingleFile 化、Ookii.Dialogs.Wpf の削除、依存バージョンの更新、`global.json` による SDK 固定、2形態の発行・起動確認・梱包のスクリプト化。Dependencies: scan-golden-baseline
- [x] scan-correctness -- 事前カウントの長いパス対応（本スキャンの長いパスは移行で解消済み）と事前カウント用 P/Invoke の除去、描画のタブごとの取り消し、開発用ダイアログの除去、例外の握りつぶし方針の是正と `Config.txt` の解析の失敗の通知。Dependencies: dotnet10-migration
- [x] scan-performance -- `FolderInfo.AddSize` の再設計（祖先への逐次 Interlocked を廃止）、`FileSystemEnumerator<T>` による1パス列挙、並列度の制御。**通常の列挙で WizTree と同等以上**（NAS、および管理者でない・NTFS 以外のローカル）を目標とし、WizTree との比較の計測手順を整える。Dependencies: scan-correctness, scan-golden-baseline
- [ ] ntfs-mft-scan -- ローカルの NTFS ドライブを管理者として走査するとき、MFT（全ファイルの目録）を直接読む走査方式を加え、**WizTree の最速モードと同等以上**を目指す。使えない条件（NAS・NTFS 以外・管理者でない）では通常の走査に戻る。Dependencies: scan-performance
- [ ] architecture-refactoring -- `MainWindow.xaml.cs` の分割、MVVM の責務整理、CommunityToolkit.Mvvm の導入、C# の新しい言語機能の適用、定数の二重管理の解消。Dependencies: scan-performance, ntfs-mft-scan
- [ ] ui-redesign -- 色・フォント・サイズのデザイントークン化、縦横レイアウト XAML の重複解消、一覧の視認性改善。Dependencies: architecture-refactoring
