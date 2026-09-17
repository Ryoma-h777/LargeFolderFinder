# Brief: architecture-refactoring

## Problem

「コードが読みづらい」という開発者の実感には、具体的な裏付けがある。

- **`Views/MainWindow.xaml.cs` が約1,260行**。全5,213行の24%が1ファイルに集中している
- **MVVM が中途半端**。`MainWindow` が自前で `INotifyPropertyChanged` を実装しつつ `MainViewModel` も存在し、状態の置き場が二重化している
- **行データを表す `FolderRowItem` が `MainWindow.xaml.cs` 内に同居**している。本来は `Models/` 相当
- **`LayoutViewBase` が `FindName("文字列")` で XAML 要素を解決**しており、XAML 側の名前を変えてもコンパイルが通る
- **定数の二重管理**。`AppConstants.LogFilesMax = 4` があるのに `Logger` 内で `int maxLogFiles = 4;` とハードコード。`MemoryOptimizeIntervalMinutes = 5` は未参照でタイマーは1分固定。**定数を変えても効かない**
- **死んだコードの残存**。`Scanner.PruneTree` は未呼び出し、`AppConstants.CacheFileName`（旧形式）も未使用、コメントアウトされた処理塊が複数
- **C# 12 を指定しながら言語機能をほぼ使っていない**。file-scoped namespace、record、コレクション式はいずれも未使用。`INotifyPropertyChanged` と `RelayCommand` は手書き
- **名前空間がディレクトリと無関係にフラット**で、`ViewModels` のみ例外という不統一

## Current State

- レイヤー別のディレクトリ構成（Models / Services / ViewModels / Views / Helpers）自体は妥当
- `Services/` は UI に依存せず、責務分離ができている
- 問題は主に `Views/` と `ViewModels/` の境界、および定数・死んだコードの管理にある
- `.NET 10` 移行済み、CommunityToolkit.Mvvm 8.4.2 のバージョン制約は `dotnet10-migration` で確定済み

## Desired Outcome

- `MainWindow.xaml.cs` が読める規模に分割されている
- 状態の置き場が一意で、MVVM の責務境界が明確
- 定数を変えれば実際に挙動が変わる
- 死んだコードがない
- C# の新しい言語機能が、可読性に寄与する範囲で使われている

## Approach

段階的に進める。一度に全面書き換えを行わない。

1. **死んだコードと定数の二重管理を先に片付ける**。リスクが低く、以降の作業の見通しが良くなる
2. **`FolderRowItem` を `Models/` へ移設**する
3. **`MainWindow.xaml.cs` を責務ごとに分割**する。何をどこへ動かすかの判断基準を先に定める（UI に依存しない処理は `Services/`、画面状態は `ViewModels/`、純粋な表示制御のみコードビハインドに残す）
4. **CommunityToolkit.Mvvm を導入**し、手書きの `INotifyPropertyChanged` / `RelayCommand` をソースジェネレータに置き換える
5. **`LayoutViewBase` の `FindName` 依存を見直す**。型安全な解決方法へ移行できるか検討する（`ui-redesign` でのレイアウト重複解消と密接に関わるため、そちらへ委ねる判断もありうる）
6. **C# の言語機能を適用**する。file-scoped namespace、record、パターンマッチなど、可読性が上がる箇所に限る
7. **名前空間の統一**を検討する。影響範囲が広いため、費用対効果を見て判断する

## Scope

- **In**:
  - 死んだコードの除去（`PruneTree`、`CacheFileName`、コメントアウトされた処理塊）
  - 定数の二重管理の解消（`LogFilesMax`、`MemoryOptimizeIntervalMinutes`）
  - `FolderRowItem` の `Models/` への移設
  - `MainWindow.xaml.cs` の分割
  - CommunityToolkit.Mvvm の導入と手書き実装の置き換え
  - C# の新しい言語機能の適用
  - 名前空間規約の統一（実施するかを含めて判断する）
  - steering（structure.md / tech.md）の更新
  - 失われた機能4件の復元（Constraints の例外を参照。2026-09-17 利用者の判断）

- **Out**:
  - 走査ロジックの変更（`scan-correctness` / `scan-performance` で完了済みが前提）
  - XAML の見た目の変更とレイアウト重複の解消（`ui-redesign` の範囲）
  - 機能の追加・変更（Constraints の例外として挙げた4件の復元を除く）

## Boundary Candidates

- 死んだコードと定数の整理
- `MainWindow.xaml.cs` の分割
- MVVM 基盤の置き換え（CommunityToolkit.Mvvm）
- 言語機能の適用と名前空間の統一

## Out of Boundary

- `Services/` 配下の設計変更（すでに責務分離ができており、触る必要がない）
- ローカライズ機構の作り替え（欠落補完は `localization-completeness` で完了済み。機構そのものの変更は本スペックの範囲だが、優先度は低い）

## Upstream / Downstream

- **Upstream**: `scan-performance`（`FolderInfo` の構造変更が確定していること。両者が同じ型に触れるため）
- **Downstream**: `ui-redesign`（整理された構造の上で見た目を作り替える）

## Constraints

- **機能の挙動を変えないこと。** 純粋なリファクタリングであり、走査結果・表示内容・操作性はすべて維持する。`scan-golden-baseline` の期待値との突き合わせを継続すること
  - **例外（2026-09-17 利用者の判断）**: 過去の要望として実装されていたのに、その後のコード変更で失われた次の3つの機能は、本スペックで元に戻す。1〜3は `.kiro/steering/decisions.md` に「食い違い」として記録されている
    - ウィンドウの位置・サイズ・状態を次回起動時に復元する（2026-02-06 のリファクタリングで読み戻す処理が削除された。公開版ではまだ動いている）
    - すでに管理者として起動しているとき、「管理者として開き直す」を押せなくする（2026-01-22 のレイアウト切り替えの実装で失われた）
    - 処理時間が1時間を超えたとき、秒まで表示する（同上）
    - 選んだ表示言語を、次回起動時に読み戻す（最初の版にはあったが、公開版 v1.0.3 の時点ですでに失われている。言語は保存されているが、起動時に読み戻す処理がない）
  - 上記4つ以外の挙動の変更は、引き続き行わないこと。復元にあたっては、`decisions.md` の該当項目を読み、記録された意図と理由に沿うこと
- **CommunityToolkit.Mvvm は 8.4.2 を使用**（8.4.0 は .NET 10 でビルド不能）。MIT ライセンス。配布物に含まれるため `ThirdPartyNotices.txt` への記載が必要
- `MainWindow.xaml.cs` は `scan-correctness`（競合修正）と `ui-redesign`（表示）も触れる共有の要所である。**直列化して競合を避ける**方針でロードマップを組んでいるため、順序を守ること
- `LayoutViewBase` の `FindName` 依存を変更する場合、**縦横2つの XAML の両方に影響する**。`ui-redesign` のレイアウト重複解消と重なるため、どちらで扱うかを設計時に明確にすること
- 名前空間の統一は影響範囲が広く、リスクに対する効果が小さい可能性がある。**やらない判断も妥当**であり、その場合は structure.md に現状追認として記録すること
- コメントは日本語で記述する（プロジェクト方針）。XML ドキュメントコメントの日本語記述は既存コードで徹底されており維持すること
- ログメッセージは英語で `AppConstants` に集約する既存方針を維持すること
