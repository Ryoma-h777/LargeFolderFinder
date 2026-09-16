# `docs/` 棚卸し

`docs/` 配下の各フォルダについて、保全の扱いを記録する表（requirements.md 要件1、design.md「棚卸し」）。

## 表の列

| 列 | 意味 |
|----|------|
| フォルダ | `docs/` 配下のフォルダ名 |
| 分類 | 内容の種類。機能追加／仕様決定／技術判断／不具合修正／却下・撤回／作業ログ／空 のいずれか |
| 扱い | 「抽出した」または「読んだが保全しない」 |
| 理由 | 「読んだが保全しない」とした判断の根拠。「抽出した」の行では、抽出対象として含むもの（要望／決定と理由／却下案／非目標）とその内容（1行） |
| 参照した `.md` | 根拠として読んだファイル名。記録本体がないフォルダは「なし」 |

## 根拠の限定

- 判断の根拠は、各フォルダの記録本体である `.md` ファイルに限る
- 編集履歴のスナップショット（`*.resolved*`）と付随するメタデータ（`*.metadata.json`）は根拠に用いない。メタデータ内の要約も用いない
- 分類はフォルダ名ではなく `.md` の内容から決める
- 記述は旧AIモデルの生成物であるため鵜呑みにせず、必要に応じて現行コードで裏を取る

## 判断の基準

- 要件2の抽出対象（利用者から出た要望、仕様上の決定とその理由、試して却下された案、明示的な非目標）が1つでも含まれるフォルダは「読んだが保全しない」にしない
- 中身のないフォルダ（ファイルもサブフォルダも持たない）は「空」とし、保全の対象外とする
- 一度きりの作業ログ、またはビルド環境・コンパイルエラーの修正に留まり、抽出対象を含まないフォルダは「読んだが保全しない」とする
- 抽出対象を含まず、記述がすべて現行のコードから読み取れる事実の再掲に留まるフォルダも「読んだが保全しない」とする（要件2.7）
- 迷った場合は保全する側に倒す

## 棚卸しの表

| フォルダ | 分類 | 扱い | 理由 | 参照した `.md` |
|----------|------|------|------|----------------|
| `Large_Folder_Finder` | 空 | 読んだが保全しない | ファイルもサブフォルダも持たない空のフォルダで、抽出できる記録がない | なし |
| `performance_measurement` | 空 | 読んだが保全しない | ファイルもサブフォルダも持たない空のフォルダで、抽出できる記録がない | なし |
| `reorder_cancellation_token` | 空 | 読んだが保全しない | ファイルもサブフォルダも持たない空のフォルダで、抽出できる記録がない | なし |
| `win32_and_sequential` | 空 | 読んだが保全しない | ファイルもサブフォルダも持たない空のフォルダで、抽出できる記録がない | なし |
| `compiler_error_fix` | 不具合修正 | 読んだが保全しない | 引数の型違いとイベントハンドラ名の不一致によるコンパイルエラーを直しただけで、要望・決定の理由・却下案・非目標を含まない | `implementation_plan.md`, `task.md`, `walkthrough.md` |
| `fix_initialize_component_error` | 作業ログ | 読んだが保全しない | ビルドキャッシュの削除と再ビルドで解消したビルド環境の一時的な不整合の記録で、ソースの変更も抽出対象も含まない | `implementation_plan.md`, `task.md`, `walkthrough.md` |
| `Fix_Init_and_Status_20260123` | 作業ログ | 読んだが保全しない | 大半が未完了のまま終わったタスク一覧のみで、決定や理由を持たず、内容は `fix_rendering_and_status_behavior` の起動時描画とステータス表示の修正と重複する | `task.md` |
| `add_restart_admin_tooltip` | 機能追加 | 読んだが保全しない | 既存メニューへの ToolTip 追加と13言語の訳文の列挙のみで、要望や理由・却下案・非目標を含まず、訳文とその適用は現行の言語ファイルとコードから読み取れる事実の再掲になる | `implementation_plan.md` |
| `Add_Menu_Bar` | 機能追加 | 抽出した | 決定と理由: Windows アプリらしい操作性のため、上部にメニューバー（ファイル: 設定を開く・終了／ヘルプ: Readme・バージョン情報）を設けた | `implementation_plan.md`, `task.md`, `walkthrough.md` |
| `Admin_Restart` | 機能追加 | 抽出した | 決定と理由: 権限の要るフォルダ（ごみ箱など）をスキャンするため「管理者として開き直す」を追加。既に管理者なら無効化し、UAC で拒否されたら今のプロセスを維持する | `implementation_plan.md`, `task.md`, `walkthrough.md` |
| `Cache_Cleanup_20260123` | 機能追加 | 抽出した | 決定と理由: ディスク使用量の増加を防ぐため、タブを閉じたときにそのセッションの保存ファイルも削除する | `implementation_plan.md`, `task.md` |
| `Cache_Optimization` | 技術判断 | 抽出した | 決定と理由: 読み書きの速さから保存形式を YAML から MessagePack に変え、起動時に不要な巨大データを読まないよう、アプリ設定とタブごとの検索結果を別ファイルに分けた | `implementation_plan.md`, `task.md`, `walkthrough.md` |
| `Cache_Result_Persistence` | 機能追加 | 抽出した | 要望: 再起動後も再スキャンせずに前回の検索結果を使いたい（将来の複数タブを見据える）／決定: 保存データを検索セッションの一覧に改め、最後の設定だけを持つフラットな構造は廃止した | `implementation_plan.md`, `task.md`, `walkthrough.md` |
| `Code_Refactoring_and_Cache` | 却下・撤回 | 抽出した | 却下案: 実装済みだった高速キャッシュモード（前回スキャン結果の再利用と、UI のチェックボックス・有効期限つきキャッシュファイル）を、正確性の確保が難しく利用者の混乱と複雑化を招くとして全削除した／非目標: スキャン時にキャッシュファイルを読み込み、有効期限つきで前回の結果を再利用して高速化するモード（正確性の確保が難しい。スキャンは常に最新を取得する）。前回の結果を保存して再起動後に表示する機能まで捨てたわけではなく、それは後の `Cache_Result_Persistence` で入った | `implementation_plan.md`, `task.md`, `walkthrough.md` |
| `Dynamic_App_Metadata` | 技術判断 | 抽出した | 決定と理由: バージョン情報のタイトル・版・著作権を言語ファイルへの直書きから外してアセンブリ属性から差し込む／キャッシュファイル名を設定項目から定数へ移す／利用者データとログの保存先をローカルのアプリデータ配下へ移す（既存データの移行は利用者に確認するとして未決。版情報のタスクは未完了のまま） | `implementation_plan.md`, `task.md`, `task_cache_filename.md`, `task_userdata_migration.md` |
| `Git_Initialization` | 技術判断 | 抽出した | 決定: MIT ライセンスで公開リポジトリとして公開し、ビルド出力一式を zip にしてリリースに添付して配布する（寄付の設定手順を含む。配布手順の節は `Runtime_Downgrade` で書き換えられた後の内容で、自己完結型（.NET 8/9 など）と比べてファイルサイズが非常に小さいことを利点として明示する。自己完結型を捨てた経緯は `Runtime_Downgrade` 行を参照。タスク一覧は未完了のまま） | `implementation_plan.md`, `task.md`, `walkthrough.md` |
| `Physical_Size` | 仕様決定 | 抽出した | 決定と理由: 「ディスク上のサイズ」の計算オプションを UI のチェックボックスから設定ファイルへ移した（チェックボックスは撤去）／正確な物理サイズの取得はファイルごとの API 呼び出しで遅くなるため既定はオフで速度を優先。計画（`UsePhysicalSize`、既定オン・UI から削除）と実施記録（`UseAccuratePhysicalSize`、既定オフ・UI 変更なし）が食い違い、設定名も違うため、1つの設定の食い違いではなく別々の設定である可能性がある | `implementation_plan.md`, `task.md`, `walkthrough.md` |
| `Runtime_Downgrade` | 技術判断 | 抽出した | 却下案: .NET 9 の自己完結型・単一ファイル発行による配布（発行設定から自己完結型・単一ファイルの指定を削除した。記録は移行先の利点として実行ファイルの劇的な軽量化を挙げる。自己完結型と比べたサイズの小ささは `Git_Initialization` の配布手順が明示する）／決定と理由: Windows 10/11 に標準搭載の .NET Framework 4.8 へ実行基盤を下げ、ランタイムの別途インストールが要らない状態のまま配布サイズを軽くした（1回目の移行。ここで作ったダイアログ用ヘルパーと旧来の名前空間構文への書き換えは `Runtime_Downgrade_v2` で撤回。タスク一覧は未完了のまま） | `implementation_plan.md`, `task.md`, `walkthrough.md` |
| `Runtime_Downgrade_v2` | 却下・撤回 | 抽出した | 却下案: 1回目の移行が「仕様が壊れた」とされ、ダイアログ用のヘルパーファイルの新設と古い構文への書き換えを取りやめ、新しい言語バージョンを保ったままやり直した／決定と理由: 利用者が DLL を意識せず EXE をコピーするだけで使えるよう依存 DLL を EXE に埋め込む単一ファイル配布とし、設定・言語ファイルを YAML に統一して JSON 依存を外した（`walkthrough.md` の中で「System.Text.Json などの不足パッケージを追加」と「JSON への依存を完全に排除」が食い違う。`task.md` は1回目と同一） | `implementation_plan.md`, `task.md`, `walkthrough.md` |
| `SavingWindowGeometry` | 仕様決定 | 抽出した | 決定と理由: 終了時にウィンドウの位置・サイズ・状態を保存して次回復元する。最大化中は元のサイズを保ち、最小化で終えても最小化のまま起動しないよう次回は通常状態で開く | `implementation_plan.md`, `task.md`, `walkthrough.md` |
| `TabbedInterface` | 機能追加 | 抽出した | 要望: 検索結果を複数のタブで保持し、タブバーはパス選択・スキャンの行の下にブラウザ風の追加／閉じるボタンで置く／決定: パス入力とスキャン操作は上部に共通で置いてアクティブなタブに作用させ、スキャン条件・表示設定・結果はタブごとに持ち、縦横のレイアウトは全タブ共通とする／却下案: スキャン時に利用者の意図によって新規タブで実行する案はとらず、アクティブなタブの結果を更新する挙動を基本とした | `implementation_plan.md`, `task.md`, `walkthrough.md` |
| `UI_Improvements_20260123` | 機能追加 | 抽出した | 要望: タブ追加ボタンを最後のタブの直後に置く、タブの最小幅、ツールチップを作成日時とパスに、結果一覧のフォントサイズ設定と保存、行の交互色と行の高さ／却下案: タブを均等幅で並べる方式をやめ左詰めにした（タブがウィンドウ幅いっぱいに広がらなくなるトレードオフを明示）。検証とウォークスルーは未完了 | `implementation_plan.md`, `task.md` |
| `Unit_Localization` | 不具合修正 | 抽出した | 決定: 単位を切り替えてもラベル・ツールチップ・スキャン中メッセージに「GB」が固定で残る問題を、言語ファイルに単位の差し込み位置を設けて直した（`Unit_Switching` のラベルから単位表記を外す決定と食い違い、前後関係は `.md` から読み取れない。タスク一覧は未完了のまま） | `implementation_plan.md`, `task.md`, `walkthrough.md` |
| `Unit_Switching` | 機能追加 | 抽出した | 要望: 利用者のレビューでの指摘により、単位切替時に入力済みの閾値を自動換算する（例: 1 GB から 1024 MB）ことと、ラベルから単位表記を外すことを加えた／決定: 単位（KB〜TB）を画面で切り替え、再スキャンせずに表示を更新し、閾値はバイト数で受け渡す／却下案: レビューの指摘で単位文字列を返す専用関数を作らず列挙値の名前をそのまま使う／非目標: 単位名の翻訳 | `implementation_plan.md`, `task.md`, `walkthrough.md` |
| `advanced_config_and_speed` | 技術判断 | 抽出した | 決定と理由: 一般利用者が不用意に触れないよう、並列スキャンなど上級者向けの設定を UI から外して設定ファイルへ移した（並列スキャンのチェックボックスは撤去）。スキャン直前に読み直して起動中の書き換えを反映し、HDD や NAS のシーク詰まりを避ける逐次スキャンを用意、事前カウントを Win32 API で高速化 | `implementation_plan.md`, `task.md`, `walkthrough.md` |
| `clipboard_copy_feature` | 機能追加 | 抽出した | 決定と理由: 結果を外部の文書やチャットへ貼れるようコピーボタンを追加。完了通知はステータスバー右側の専用領域に数秒だけ出し、左側の進捗や経過時間の表示を消さない／却下案: コピーボタンのアイコンを絵文字で済ませる案はとらず、モダンなアプリらしく XAML の `Path` で描く | `implementation_plan.md`, `task.md`, `walkthrough.md` |
| `config_extension_to_txt` | 仕様決定 | 抽出した | 決定と理由: 関連付けがなくてもダブルクリックでメモ帳などで開けるよう設定ファイルの拡張子を .txt にし、中身はコメントを書ける YAML のまま保つ | `implementation_plan.md`, `task.md`, `walkthrough.md` |
| `config_open_button` | 機能追加 | 抽出した | 決定: 上級者向け設定ファイルを既定のエディタで開く目立たない歯車ボタンを置き、ファイルがなければ既定値で生成してから開く | `implementation_plan.md`, `task.md`, `walkthrough.md` |
| `fix_rendering_and_status_behavior` | 不具合修正 | 抽出した | 決定と理由: 起動時に保存済みの結果が描画されない問題（初期化順序）と、連続して描画すると「描画中…」が残る問題を直し、描画のキャンセルをタブごとに持たせてタブ間の干渉をなくした | `implementation_plan.md`, `task.md`, `walkthrough.md` |
| `fix_scan_completion_display` | 不具合修正 | 抽出した | 決定: フォルダ名に反して中身はフォルダ選択ダイアログが開かない問題（COM インターフェース定義の省略によるメソッド順のずれ）の修正／要望: 利用者の要望でダイアログ呼び出しに例外の捕捉とログ記録を加えた | `implementation_plan.md`, `task.md`, `walkthrough.md` |
| `layout_switching_feature` | 機能追加 | 抽出した | 決定: 表示メニューから縦並びと横並び（設定を左、結果を右）を切り替え、選択を保存して次回も維持する。「ファイルも含める」の表記を「ファイルも表示」に改めた（タスク一覧は未完了のまま） | `implementation_plan.md`, `task.md`, `walkthrough.md` |
| `refactor_layout_views` | 技術判断 | 抽出した | 決定と理由: 縦・横2つのレイアウトビューで重複していた処理を共通の基底クラスに集約して保守性を上げた（手動での UI 確認は未実施） | `implementation_plan.md`, `task.md`, `walkthrough.md` |
| `restore_sessions` | 仕様決定 | 抽出した | 決定: 設定ファイルが消えたり壊れたりしても、保存済みのセッションファイルがあればファイル名（作成日時）順に読んでタブを復元し、1つもないときだけ既定のタブを作る／非目標: その場合のウィンドウ位置や言語など設定の復元 | `implementation_plan.md`, `walkthrough.md` |
| `scan_optimization` | 技術判断 | 抽出した | 決定と理由: 開始前の待ち時間を縮めるため事前カウントを4階層までに限り、進捗もその範囲で数える。残り時間は開始時からの平均では速度変動で不安定なため、直近の速度を重視する指数移動平均で推定する | `implementation_plan.md`, `task.md`, `walkthrough.md` |
| `separator_switching_improvement` | 機能追加 | 抽出した | 決定: 区切り（タブ／スペース）を切り替えたら再スキャンせず、メモリに保持した直近の結果から即座に再描画する | `implementation_plan.md`, `task.md`, `walkthrough.md` |
| `settings_and_status` | 機能追加 | 抽出した | 決定: 利便性のため検索サイズを次回起動時に引き継ぎ、完了時に処理時間を経過時間の長さに応じた形式（時間・分・秒・ミリ秒）でステータスバーに表示する | `implementation_plan.md`, `task.md`, `walkthrough.md` |
| `skip_count_feature` | 機能追加 | 抽出した | 決定と理由: カウントを待たずに始められるよう事前カウントを省く設定（既定オフ）を追加。総数が不明なときは進捗バーを不確定表示にし、残り時間は計算できないため処理数と経過時間だけを出す | `implementation_plan.md`, `task.md`, `walkthrough.md` |
| `startup_fix_and_stability` | 不具合修正 | 抽出した | 決定と理由: 初期化中に発火したイベントが未生成のコントロールに触れて起動しなくなった問題にガードを加え、起動時の例外は内容を表示して原因不明のままアプリが消えないようにした | `implementation_plan.md`, `task.md`, `walkthrough.md` |
| `ui_layout_adjustment` | 仕様決定 | 抽出した | 要望: スキャン実行に関する行と表示内容の調整に関する行を分けて境界線で区切り、表示設定の行は通常の約8割の高さに詰める | `implementation_plan.md`, `task.md`, `walkthrough.md` |
| `unit_redefinition` | 仕様決定 | 抽出した | 要望: 1000進（KB 系）と1024進（KiB 系）の単位を使い分けたい／決定と理由: 既定の単位は Windows の計算方式に合わせて1024進の GiB とする | `implementation_plan.md`, `task.md`, `walkthrough.md` |
| `update_translations` | 仕様決定 | 抽出した | 要望: 利用者の指示で、UI の表示崩れを防ぐため各言語の訳文とラベルを短くする／決定: 検索サイズのツールチップに「0 も指定できるが件数が多いと重くなる」旨を示す | `implementation_plan.md`, `task.md`, `walkthrough.md` |
| `yaml_config_migration` | 技術判断 | 抽出した | 決定と理由: 設定の意味を外部文書なしで説明できるよう、コメントを書ける YAML へ設定ファイルの形式を JSON から移した | `implementation_plan.md`, `task.md`, `walkthrough.md` |
