# 抽出元と抽出先の対応

このスペックで保全した各記述が、旧AIモデルとの開発記録のどのファイルから来たかを辿るための表（requirements.md 要件5、design.md「対応関係」）。

- `inventory.md` は、記録のフォルダごとに「抽出した」「読んだが保全しない」の扱いを決めた表である。この表は、そのうち「抽出した」34フォルダの記述が、保全先のどのファイルのどの節に入ったかを、記述の単位で示す
- 疑問が生じたときは、この表で抽出元のファイルを特定し、原文を確かめる。ただし原文は Git 管理外の唯一の写しであり、失われた場合に何が参照できなくなるかを最後の節に記す

## 表記

- 抽出元は、リポジトリ直下の `docs/` 配下からの相対で「フォルダ名／ファイル名」と書く。`docs/` は表では省く。1つのセルに複数のファイルを挙げるときは読点で区切る
- 抽出先は「ファイル名／節／項目の見出し」と書く。ファイルはすべて `.kiro/steering/` 配下にある。`decisions.md` の項目の見出しは、末尾の照合結果（［確認済み］など）を除いて写す
- 内容の要約は、保全先に書かれている内容だけを1文で示す。抽出元にだけ書かれていることは書かない
- 1つの項目に、役割の違う複数のフォルダが寄与している場合は、フォルダごとに行を分ける

## `decisions.md` の対応

### 一度試して捨てた案

| 抽出元（フォルダ／ファイル） | 抽出先（ファイル／節） | 内容の要約 |
|------------------------------|------------------------|------------|
| `Code_Refactoring_and_Cache/implementation_plan.md`, `Code_Refactoring_and_Cache/walkthrough.md` | `decisions.md`／一度試して捨てた案／高速キャッシュモード（スキャン時に前回の結果を再利用する） | スキャン時に前回の結果を有効期限つきで再利用するモードを、正確性・利用者の混乱・複雑化を理由に全削除し、同じ形では再提案しないこと |
| `Cache_Result_Persistence/implementation_plan.md` | `decisions.md`／一度試して捨てた案／高速キャッシュモード（スキャン時に前回の結果を再利用する） | 捨てたのはスキャン時の再利用だけで、前回の結果を保存して再起動後に表示する機能は別の要望で入ったという範囲の注意 |
| `Runtime_Downgrade/implementation_plan.md`, `Runtime_Downgrade/walkthrough.md` | `decisions.md`／一度試して捨てた案／実行基盤の移行に伴って外れた、.NET 9 の自己完結型・単一ファイルの発行設定 | .NET Framework 4.8 への移行に伴って自己完結型・単一ファイルの指定が発行設定から外れたが、配布形態として評価して退けた記録はないこと |
| `Git_Initialization/walkthrough.md` | `decisions.md`／一度試して捨てた案／実行基盤の移行に伴って外れた、.NET 9 の自己完結型・単一ファイルの発行設定 | 配布手順の記録が、自己完結型と比べたファイルサイズの小ささを利点として明示していること |
| `Runtime_Downgrade_v2/walkthrough.md` | `decisions.md`／一度試して捨てた案／実行基盤の移行に伴って外れた、.NET 9 の自己完結型・単一ファイルの発行設定 | 移行後は依存 DLL を EXE に埋め込む形で単一ファイルの配布を実現しているという補足 |
| `Runtime_Downgrade/walkthrough.md`, `Runtime_Downgrade_v2/walkthrough.md` | `decisions.md`／一度試して捨てた案／古い実行基盤に合わせてコードの書き方を古くする移行（1回目の移行） | 1回目の移行でブロック形式の名前空間へ書き換えたことが「仕様が壊れた」課題になり、やり直しでは新しい言語バージョンで新しい構文を保ったこと |
| `fix_scan_completion_display/implementation_plan.md`, `fix_scan_completion_display/walkthrough.md` | `decisions.md`／一度試して捨てた案／古い実行基盤に合わせてコードの書き方を古くする移行（1回目の移行） | 別の記録にダイアログ専用のファイルが出てくるので、ダイアログ処理を分けることはこの項目の対象外という範囲の注意 |
| `UI_Improvements_20260123/implementation_plan.md`, `UI_Improvements_20260123/task.md` | `decisions.md`／一度試して捨てた案／タブを均等幅で並べる | 追加ボタンを最後のタブの直後に置く要望のため、均等幅をやめて左詰めにし、最小幅を設けたこと |
| `Unit_Switching/walkthrough.md` | `decisions.md`／一度試して捨てた案／小さな選択（利用者のレビューや計画の段階で退けたもの） | 単位の表示に専用の文字列変換関数を作る案を、利用者のレビューで取りやめたこと |
| `clipboard_copy_feature/implementation_plan.md`, `clipboard_copy_feature/walkthrough.md` | `decisions.md`／一度試して捨てた案／小さな選択（利用者のレビューや計画の段階で退けたもの） | コピーボタンのアイコンを絵文字で済ませず、XAML の図形で描いたこと |
| `TabbedInterface/task.md` | `decisions.md`／一度試して捨てた案／小さな選択（利用者のレビューや計画の段階で退けたもの） | スキャンを新規タブで実行する案をとらず、「今回は」アクティブなタブを更新する挙動を基本としたこと |

### 明示的な非目標（やらないと決めたこと・求めないこと）

| 抽出元（フォルダ／ファイル） | 抽出先（ファイル／節） | 内容の要約 |
|------------------------------|------------------------|------------|
| `Code_Refactoring_and_Cache/walkthrough.md` | `decisions.md`／明示的な非目標／スキャン時に保存済みの結果を再利用して高速化すること | スキャンは常に最新を取得し、前回の結果の保存と再起動後の表示は非目標に含まないこと |
| `advanced_config_and_speed/implementation_plan.md`, `advanced_config_and_speed/walkthrough.md` | `decisions.md`／明示的な非目標／上級者向けの設定を画面に出すこと | 一般の利用者が不用意に触れないよう、並列スキャンのチェックボックスを画面から外して設定ファイルで制御すること |
| `Physical_Size/implementation_plan.md`, `Physical_Size/walkthrough.md` | `decisions.md`／明示的な非目標／上級者向けの設定を画面に出すこと | 「ディスク上のサイズ」も設定ファイルへ移す計画だったが画面から外す理由は書かれず、計画と実施記録で設定の名前と既定値が食い違うこと |
| `Unit_Switching/task.md` | `decisions.md`／明示的な非目標／単位の名前を翻訳すること | 単位名は英語表記のまま使い、記録は「翻訳不要と判断」とだけ書くこと |
| `restore_sessions/implementation_plan.md`, `restore_sessions/walkthrough.md` | `decisions.md`／明示的な非目標／設定ファイルが失われたときに、設定まで復元すること | 設定ファイルが失われてセッションからタブを復元するとき、ウィンドウの位置・サイズや言語設定は初期値に戻ってよいこと |

### 決定とその理由

| 抽出元（フォルダ／ファイル） | 抽出先（ファイル／節） | 内容の要約 |
|------------------------------|------------------------|------------|
| `advanced_config_and_speed/implementation_plan.md`, `advanced_config_and_speed/task.md`, `advanced_config_and_speed/walkthrough.md` | `decisions.md`／決定とその理由／上級者向けの設定は、起動したままでも書き換えを反映する | 起動したまま試行錯誤できるよう、設定ファイルをスキャンの開始時にも読み直し、エディタで開いたままでも読めるようにしたこと |
| `config_open_button/implementation_plan.md`, `config_open_button/walkthrough.md` | `decisions.md`／決定とその理由／設定ファイルを開くボタンは目立たせない | 設定ファイルを既定のエディタで開くボタンを置き、画面の邪魔にならないよう小さく目立たない外観にしたこと |
| `Cache_Cleanup_20260123/implementation_plan.md`, `Cache_Cleanup_20260123/task.md` | `decisions.md`／決定とその理由／タブを閉じたら、そのタブの保存ファイルも消す | ディスク使用量が増え続けるのを防ぐため、タブを閉じたらその保存ファイルを削除すること |
| `SavingWindowGeometry/implementation_plan.md`, `SavingWindowGeometry/walkthrough.md` | `decisions.md`／決定とその理由／ウィンドウの状態は、最小化のままでは復元しない | 終了時の位置・サイズ・状態を次回に復元し、最小化のまま起動しないようにする決定と、現行コードに読み戻す処理がない食い違い |
| `startup_fix_and_stability/implementation_plan.md`, `startup_fix_and_stability/walkthrough.md` | `decisions.md`／決定とその理由／起動時の例外は、内容を画面に出す | 原因不明のままアプリが消えないよう初期化の例外を表示し、初期化中のイベントはコントロールが揃うまで処理を飛ばすこと |
| `Admin_Restart/implementation_plan.md`, `Admin_Restart/task.md`, `Admin_Restart/walkthrough.md` | `decisions.md`／決定とその理由／管理者として開き直す | 権限の要るフォルダのスキャンのため管理者として開き直せるようにする決定と、管理者のときに項目を無効にする処理が現行コードにない食い違い |
| `Add_Menu_Bar/implementation_plan.md`, `Add_Menu_Bar/walkthrough.md` | `decisions.md`／決定とその理由／メニューバーを置く | Windows アプリらしい標準的な操作性のため、メニューバーを置いたこと |
| `TabbedInterface/implementation_plan.md`, `TabbedInterface/task.md` | `decisions.md`／決定とその理由／タブと共通操作の分担 | パス入力とスキャン操作をタブの外に共通で置き、スキャン条件・表示設定・結果はタブごとに持つと計画が判断したこと |
| `fix_rendering_and_status_behavior/implementation_plan.md`, `fix_rendering_and_status_behavior/walkthrough.md` | `decisions.md`／決定とその理由／描画の取り消しを、タブ間で干渉させない | タブ間の干渉を避けるため描画の取り消しをタブごとに持つ決定と、現行の描画処理がそれを使っていない食い違い |
| `clipboard_copy_feature/implementation_plan.md`, `clipboard_copy_feature/walkthrough.md` | `decisions.md`／決定とその理由／コピー完了の通知は、進捗の表示を消さない | 進捗や経過時間の表示を保つため、コピー完了の通知を状態表示の右側の専用領域に短く出すこと |
| `scan_optimization/implementation_plan.md`, `scan_optimization/walkthrough.md` | `decisions.md`／決定とその理由／進捗率は、事前カウントした範囲で数える | 進捗率と事前カウントの数を整合させるため、進捗も事前カウントした範囲のフォルダだけを数えること |
| `skip_count_feature/implementation_plan.md`, `skip_count_feature/walkthrough.md` | `decisions.md`／決定とその理由／事前カウントを省いたときは、残り時間を出さない | 総数が分からず計算できないため、事前カウントを省いたときは残り時間ではなく経過時間を出すこと |
| `settings_and_status/implementation_plan.md`, `settings_and_status/walkthrough.md` | `decisions.md`／決定とその理由／完了時の処理時間と、検索サイズの引き継ぎ | 処理時間を長さに応じた形で表示し検索サイズを引き継ぐ決定と、1時間を超えると秒を出さない現行コードとの食い違い |
| `yaml_config_migration/walkthrough.md`, `Runtime_Downgrade_v2/walkthrough.md` | `decisions.md`／決定とその理由／設定の YAML 化に付随して外れた JSON 依存（記録どうしが食い違う） | YAML への移行と統一に付随して JSON への依存を排除したと記録されるが、評価して JSON を避けた決定ではないこと |
| `Runtime_Downgrade/walkthrough.md`, `Runtime_Downgrade_v2/walkthrough.md` | `decisions.md`／決定とその理由／設定の YAML 化に付随して外れた JSON 依存（記録どうしが食い違う） | JSON のパッケージを追加したとする記述と排除したとする記述が食い違い、最終の状態は記録から判断できないこと |

### 要望に由来する仕様

| 抽出元（フォルダ／ファイル） | 抽出先（ファイル／節） | 内容の要約 |
|------------------------------|------------------------|------------|
| `Cache_Result_Persistence/implementation_plan.md`, `Cache_Result_Persistence/walkthrough.md` | `decisions.md`／要望に由来する仕様／再起動後も、再スキャンせずに前回の検索結果を使う | 再起動後も再スキャンせず前回の結果を使いたい要望から、検索セッションごとに条件と結果を保存するようにしたこと |
| `Cache_Optimization/walkthrough.md` | `decisions.md`／要望に由来する仕様／再起動後も、再スキャンせずに前回の検索結果を使う | 1つのファイル内のセッションの一覧という保存の構造が、後にタブごとのファイルへの分割に置き換わったという補足 |
| `TabbedInterface/implementation_plan.md` | `decisions.md`／要望に由来する仕様／タブバーは、パス選択とスキャンの行より下に置く | タブバーをパス選択とスキャンの行より下に置く要望と、現行では上にあり利用者の判断で現行を正としたこと |
| `UI_Improvements_20260123/implementation_plan.md`, `UI_Improvements_20260123/task.md` | `decisions.md`／要望に由来する仕様／タブと結果一覧の見た目 | タブの追加ボタンの位置・最小幅・ツールチップ、結果一覧のフォントサイズ・交互の背景色・行の高さについての要望 |
| `ui_layout_adjustment/implementation_plan.md`, `ui_layout_adjustment/walkthrough.md` | `decisions.md`／要望に由来する仕様／スキャンの行と表示設定の行を分ける | スキャンの行と表示設定の行を境界線で分け、表示設定の行を低く詰める要望と、一度実装された線と縮小が現行コードで失われている食い違い |
| `TabbedInterface/implementation_plan.md`, `TabbedInterface/task.md` | `decisions.md`／要望に由来する仕様／スキャンの行と表示設定の行を分ける | 後のタブ化で操作の置き場所が移る計画になったという補足 |
| `Unit_Switching/implementation_plan.md`, `Unit_Switching/walkthrough.md` | `decisions.md`／要望に由来する仕様／単位を切り替えたら閾値を換算し、ラベルから単位を外す（レビューでの指摘） | レビューでの指摘による閾値の自動換算とラベルからの単位の除去、および閾値を換算しない現行を利用者の判断で正としたこと |
| `Unit_Localization/implementation_plan.md`, `Unit_Localization/task.md`, `Unit_Localization/walkthrough.md` | `decisions.md`／要望に由来する仕様／単位を切り替えたら閾値を換算し、ラベルから単位を外す（レビューでの指摘） | ラベルに選択中の単位を出す形に直した記録が、ラベルから単位を外す指摘と逆で、前後関係が記録から読み取れないこと |
| `unit_redefinition/implementation_plan.md`, `unit_redefinition/walkthrough.md` | `decisions.md`／要望に由来する仕様／1000進と1024進の単位を使い分ける | 1000進と1024進の単位を使い分ける要望と、実装されず利用者の判断で取り下げたこと |
| `Unit_Switching/implementation_plan.md`, `Unit_Switching/walkthrough.md`, `Unit_Switching/task.md` | `decisions.md`／要望に由来する仕様／1000進と1024進の単位を使い分ける | 要望より前は KB〜TB の4種で GB を1024進として換算していたという補足 |
| `update_translations/implementation_plan.md`, `update_translations/task.md`, `update_translations/walkthrough.md` | `decisions.md`／要望に由来する仕様／指示のあった訳文とラベルを短くする | 表示崩れを防ぐため指示のあったキーとラベルの訳文を短くする利用者の指示と、検索サイズのツールチップの文面 |
| `fix_scan_completion_display/implementation_plan.md`, `fix_scan_completion_display/walkthrough.md` | `decisions.md`／要望に由来する仕様／フォルダ選択ダイアログの失敗をログに残す | ダイアログが開かない不具合を直した際に、要望で呼び出しの例外を捕まえてログに残すようにしたこと |

## 既存の steering に補った理由と訂正

タスク3.1で、`decisions.md` に重ねて書かず、既存の steering の該当する記述の側に補った理由と訂正である。

### 記録に由来するもの

| 抽出元（フォルダ／ファイル） | 抽出先（ファイル／節） | 内容の要約 |
|------------------------------|------------------------|------------|
| `config_extension_to_txt/implementation_plan.md`, `config_extension_to_txt/walkthrough.md` | `tech.md`／設定・データの保存先／動作設定（`Config.txt`）の行 | 拡張子を `.txt` にしたのは、アプリが関連付けられていなくてもダブルクリックでメモ帳などですぐ開けるようにするためであること |
| `yaml_config_migration/implementation_plan.md`, `yaml_config_migration/walkthrough.md` | `tech.md`／設定・データの保存先／動作設定（`Config.txt`）の行 | 中身を YAML にしたのはコメントで設定の意味が分かるようにするための当時の理由で、現行の `Config.txt` は説明を同梱の Readme に委ねていること |
| `Cache_Optimization/implementation_plan.md`, `Cache_Optimization/walkthrough.md` | `tech.md`／主要な技術判断とその理由／MessagePack + LZ4 | MessagePack は JSON や YAML より読み書きが速くコンパクトなため採り、起動時に不要な巨大データを読まないようアプリ設定とタブごとの結果を別ファイルに分けたこと |
| `Unit_Switching/task.md` | `tech.md`／主要な技術判断とその理由／単位名を訳さない例外 | ローカライズには、単位名を訳さないと決めた例外があり、`decisions.md` の該当項目を指すこと |
| `advanced_config_and_speed/walkthrough.md` | `performance.md`／並列処理のパターン／逐次実行 | 逐次実行は HDD のシークの詰まりを避けるために設けた経路で、シークの詰まりが疑われる NAS では試す余地があること |
| `scan_optimization/implementation_plan.md`, `scan_optimization/walkthrough.md` | `performance.md`／進捗通知のスロットリング／指数移動平均 | 進み具合によって速度が変わり開始時からの平均では予測が不安定になるため、直近の速度を重視して推定すること（推測で書かれていた理由の訂正） |
| `refactor_layout_views/implementation_plan.md`, `refactor_layout_views/walkthrough.md` | `structure.md`／レイアウト切り替えのパターン／`LayoutViewBase` | 2つのビューで重複していた処理をなくし保守性を上げるために設けた基底クラスであること |

### 記録ではなく、現行のコードや `docs/` の実態に由来する訂正

抽出元が記録のファイルではないため、上の表とは分けて示す。記録が失われても、これらの根拠は失われない。

| 根拠 | 抽出先（ファイル／節） | 内容の要約 |
|------|------------------------|------------|
| 現行コードとコードの履歴（タスク2.5の照合） | `tech.md`／設定・データの保存先／アプリ設定（`Settings.msgpack`）の行 | 言語とウィンドウの位置・サイズ・状態は保存されるだけで現状は起動時に読み戻しておらず、`architecture-refactoring` で復元すること |
| 現行コード中のコメント | `tech.md`／主要な技術判断とその理由／MessagePack + LZ4、`performance.md`／メモリ | LZ4 圧縮によるデータ量の「50〜70%削減」はコード中のコメントにしか出典がなく、計測の記録はないこと |
| `docs/` 配下のフォルダの実数（`inventory.md` の42行） | `roadmap.md`／Constraints／資料の保全、`roadmap.md`／Specs (dependency order) | 記録のフォルダ数を41から42に訂正したこと |

## 抽出したフォルダの対応先

`inventory.md` で「抽出した」とした34フォルダが、上の表のどこに現れるかを示す。3フォルダは保全先に対応を持たないので、その理由を添える。

| フォルダ | 対応先 |
|----------|--------|
| `Add_Menu_Bar` | `decisions.md`／メニューバーを置く |
| `Admin_Restart` | `decisions.md`／管理者として開き直す |
| `Cache_Cleanup_20260123` | `decisions.md`／タブを閉じたら、そのタブの保存ファイルも消す |
| `Cache_Optimization` | `decisions.md`／再起動後も、再スキャンせずに前回の検索結果を使う、`tech.md`／MessagePack + LZ4 |
| `Cache_Result_Persistence` | `decisions.md`／高速キャッシュモード、再起動後も、再スキャンせずに前回の検索結果を使う |
| `Code_Refactoring_and_Cache` | `decisions.md`／高速キャッシュモード、スキャン時に保存済みの結果を再利用して高速化すること |
| `Dynamic_App_Metadata` | 対応なし。3つの決定（バージョン情報の取得元、キャッシュファイル名の定数化、保存先の移動）は記録に理由がなく手順だけなので、要件2.7により載せなかった（`tasks.md` の Implementation Notes に記録あり） |
| `Git_Initialization` | `decisions.md`／実行基盤の移行に伴って外れた、.NET 9 の自己完結型・単一ファイルの発行設定 |
| `Physical_Size` | `decisions.md`／上級者向けの設定を画面に出すこと |
| `Runtime_Downgrade` | `decisions.md`／自己完結型・単一ファイルの発行設定、古い実行基盤に合わせてコードの書き方を古くする移行、JSON 依存 |
| `Runtime_Downgrade_v2` | `decisions.md`／自己完結型・単一ファイルの発行設定、古い実行基盤に合わせてコードの書き方を古くする移行、JSON 依存 |
| `SavingWindowGeometry` | `decisions.md`／ウィンドウの状態は、最小化のままでは復元しない |
| `TabbedInterface` | `decisions.md`／小さな選択、タブと共通操作の分担、タブバーは、パス選択とスキャンの行より下に置く、スキャンの行と表示設定の行を分ける |
| `UI_Improvements_20260123` | `decisions.md`／タブを均等幅で並べる、タブと結果一覧の見た目 |
| `Unit_Localization` | `decisions.md`／単位を切り替えたら閾値を換算し、ラベルから単位を外す |
| `Unit_Switching` | `decisions.md`／小さな選択、単位の名前を翻訳すること、単位を切り替えたら閾値を換算し、ラベルから単位を外す、1000進と1024進の単位を使い分ける、`tech.md`／単位名を訳さない例外 |
| `advanced_config_and_speed` | `decisions.md`／上級者向けの設定を画面に出すこと、上級者向けの設定は、起動したままでも書き換えを反映する、`performance.md`／逐次実行 |
| `clipboard_copy_feature` | `decisions.md`／小さな選択、コピー完了の通知は、進捗の表示を消さない |
| `config_extension_to_txt` | `tech.md`／動作設定（`Config.txt`）の行 |
| `config_open_button` | `decisions.md`／設定ファイルを開くボタンは目立たせない |
| `fix_rendering_and_status_behavior` | `decisions.md`／描画の取り消しを、タブ間で干渉させない |
| `fix_scan_completion_display` | `decisions.md`／古い実行基盤に合わせてコードの書き方を古くする移行、フォルダ選択ダイアログの失敗をログに残す |
| `layout_switching_feature` | 対応なし。記録は縦並びと横並びの切り替えと選択の保存を手順として書くだけで理由を持たず、切り替えの構造は `structure.md`「レイアウト切り替えのパターン」に、レイアウトの保存は `tech.md` の `Settings.msgpack` の行に既出のため、要件2.7により載せなかった（この判断は `tasks.md` に明記がなく、本タスクで記録と既存の steering を読み合わせて確かめた） |
| `refactor_layout_views` | `structure.md`／`LayoutViewBase` |
| `restore_sessions` | `decisions.md`／設定ファイルが失われたときに、設定まで復元すること |
| `scan_optimization` | `decisions.md`／進捗率は、事前カウントした範囲で数える、`performance.md`／指数移動平均 |
| `separator_switching_improvement` | 対応なし。記録にある目的（区切りを変えたら再スキャンせず即座に表示を更新する）は、`product.md`「コア機能」の再スキャン不要の表示変更と「設計上の優先順位」の再スキャンを強いないことに既出の方針の一例で、それ以上の理由を持たないため、要件2.7により載せなかった（この判断は `tasks.md` に明記がなく、本タスクで記録と既存の steering を読み合わせて確かめた） |
| `settings_and_status` | `decisions.md`／完了時の処理時間と、検索サイズの引き継ぎ |
| `skip_count_feature` | `decisions.md`／事前カウントを省いたときは、残り時間を出さない |
| `startup_fix_and_stability` | `decisions.md`／起動時の例外は、内容を画面に出す |
| `ui_layout_adjustment` | `decisions.md`／スキャンの行と表示設定の行を分ける |
| `unit_redefinition` | `decisions.md`／1000進と1024進の単位を使い分ける |
| `update_translations` | `decisions.md`／指示のあった訳文とラベルを短くする |
| `yaml_config_migration` | `decisions.md`／JSON 依存、`tech.md`／動作設定（`Config.txt`）の行 |

## 抽出元が失われた場合に参照できなくなるもの

`docs/` は Git 管理外で、ほかに写しがない。保全先に残したのは要約と理由だけなので、失われると次のものは参照できなくなる。表の対応はフォルダ名とファイル名までを示すだけで、原文の代わりにはならない。

### 原文でしか分からない言い回しと文脈

- 要望・指示・レビューでの指摘の原文。保全先は「利用者の要望」「レビューでの指摘」と明示された範囲に絞って要約したので、どの言葉で求められたか、どこまでを求めたかの微妙な幅は原文でしか確かめられない。とくに `update_translations` の計画にある、対象キーの日本語の原文、短縮を確認するラベルの一覧（「特に指示のあった項目」と添えた範囲）、英語を基準にした訳文の方針。`Unit_Switching` のレビューで閾値の換算を求めた際の具体例
- 1回目の実行基盤の移行が「仕様が壊れた」とされた前後の文脈（`Runtime_Downgrade`、`Runtime_Downgrade_v2`）。記録自体が何が壊れたかを具体的に書いていないので、手がかりはやり直しの計画と実施記録に並ぶ変更点の違いだけであり、それも失われる
- `UI_Improvements_20260123` が利用者の確認事項として示した、タブがウィンドウ全体に広がらなくなるトレードオフの文面。保全先にはトレードオフが利用者の確認事項として示されたことと、検証とウォークスルーが未完了のまま終わったことを残したが、トレードオフの文面そのものと、未完了の根拠であるタスク一覧の項目は原文にしかない
- `TabbedInterface` が検討事項として残した「パスが変わっている場合」の扱いの原文と、「今回は」と限定した範囲

### 当時の値と識別子

- 保全先は値ではなく理由を残す方針なので、当時の値は残していない。事前カウントの当時の階層数（4）と指数移動平均の当時の係数（0.2）は `tasks.md` の Implementation Notes に（階層数は `inventory.md` にも）書き留めてあるが、それ以外の値、たとえば捨てた高速キャッシュモードで削除した有効期限の定数やキャッシュ用のファイル名は原文にしかない
- `Physical_Size` の計画と実施記録で食い違う2つの設定名とそれぞれの既定値、UI を変えたかどうかの原文。保全先は「食い違う」ことだけを残し、別々の設定である可能性の判断材料は原文にある
- 当時のファイル構成とクラス名、メソッド名。各計画は変更したファイルを列挙しているが、現行の構成とは異なり、保全先には写していない

### 記録どうしの食い違いの原文

- `Runtime_Downgrade_v2/walkthrough.md` の中で、JSON のパッケージを「追加」したとする文と、JSON への依存を「完全に排除」したとする文。保全先は食い違いがあることと、現行コードがどちらと一致するかだけを残している
- `Unit_Switching` のラベルから単位を外す指摘と、`Unit_Localization` のラベルに単位を出す修正。前後関係を記録から読み取れないと判断した根拠（タスク一覧が未完了、実施記録は完了と報告）の原文
- `Admin_Restart` のタスク一覧で、UI の節が「無効化または非表示（オプション）」、ロジックの節が無効化と書く揺れの原文

### 載せなかった記述

- 理由がなく手順だけだったため載せなかった決定の中身。`Dynamic_App_Metadata` の3つの決定の手順と、利用者データの保存先を移す際に既存データを移行するかを利用者に確認するとしたこと（確認の結果は記録にない）。`layout_switching_feature` の横並び時のボタンの配置（設定を開くボタンとコピーボタンを結果の領域の右上にまとめる）や、「ファイルも含める」を「ファイルも表示」に改めた表記の変更。`separator_switching_improvement` の再描画の組み立て
- `Git_Initialization` の公開と配布の手順の細部（ライセンスの準備、リポジトリの公開設定、リリースへの添付の手順、寄付の設定手順）。保全先に残したのは、配布手順が自己完結型と比べたサイズの小ささを利点とする一点だけである
- 保全しないと判断した8フォルダのうち、中身のある4フォルダの記録（`compiler_error_fix` の修正内容、`fix_initialize_component_error` の解消の手順、`Fix_Init_and_Status_20260123` の未完了のタスク一覧、`add_restart_admin_tooltip` の13言語の訳文）。空の4フォルダは名前だけが `inventory.md` に残り、失われるものはない
- 各フォルダの検証計画にある手動確認の手順。回帰の確認に使える観点を含むが、保全の対象外とした

### 根拠に用いなかったファイル

- 編集履歴のスナップショット（`*.resolved*`）と付随するメタデータ（`*.metadata.json`）。保全先はこれらを根拠に用いていないので、保全先の記述の裏付けは失われない。スナップショットはフォルダ横断で同一の連番チェーンであり固有の情報を持たないと判断したが、記録本体が書き上がるまでの途中の版は、これらが失われると参照できなくなる

### 失われても残るもの

- 各フォルダの名前、分類、扱い、抽出対象の1行の要約（`inventory.md`）
- 保全した決定・理由・却下案・非目標・要望の要約と、現行コードとの照合結果（`decisions.md` と既存の steering）
- 抽出元のフォルダとファイルの名前と、保全先との対応（この表）
