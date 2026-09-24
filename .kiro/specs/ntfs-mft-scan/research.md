# Research & Design Decisions

## Summary
- **Feature**: `ntfs-mft-scan`
- **Discovery Scope**: Complex Integration（OS の直接呼び出しを使う新しい走査の経路を、既存の走査と並べて入れる）
- **Key Findings**:
  - **MFT を生で解析する必要はない**。`FSCTL_QUERY_FILE_LAYOUT`（Windows 8.1 以降、NTFS 専用の文書化された呼び出し）が、ボリューム全体のファイルについて **名前・親のファイル参照番号・属性・ストレームごとの論理サイズと確保サイズ** を 1 回の連続した列挙で返す。ハードリンクの別名は名前ごとに、8.3 の短縮名は印つきで返るため、利用者の決定（名前ごとに数える・短縮名で二重に数えない）をそのまま満たせる
  - 生の MFT 解析（`FSCTL_GET_NTFS_VOLUME_DATA` → $MFT のデータラン → ファイルレコードの解析）は最速だが、update sequence の補正、$MFT 自身の断片化、拡張レコード、8.3 の別名など落とし穴が多い。**二次の手段**として残す
  - `FSCTL_ENUM_USN_DATA`（Everything の方式）は**サイズを返さない**。1件ずつ補うと 134 万ファイルで約120秒という報告があり、単独では採らない
  - どの方式もボリュームのハンドルを開くため**管理者が必須**（ボリュームのデバイスの既定の権限が管理者に限られる）。**2026-09-24 に管理者でない状態で実測して確定した**（下記「2026-09-24 の実機の確認」）。ボリュームの生の読み取りは NTFS のファイル単位の権限の確認を通らないので、**アクセス権の無い場所も数えられる**（利用者の決定どおり）
  - WizTree の速さは「ファイル1件ずつ OS に尋ねる往復」を「目録の1回の連続した読み取り」に置き換えたことによる。100万ファイルなら目録は約1GB で、NVMe なら読み取り自体は1秒未満
  - **ただし 2026-09-24 の実測で、この説明だけでは足りないことが分かった**。WizTree は**管理者でない状態でも 3.67〜4.59 秒**で終わり、管理者でも速度が変わらなかった。管理者でなければ目録は読めない（実測で確定）ので、**WizTree の速さの主因は目録の方式ではない**。要件1.2 の前提に関わるため、人間の判断を仰ぐ（下記「判断」）

## Research Log

### 方式の候補
- **Sources**: learn.microsoft.com（`FSCTL_QUERY_FILE_LAYOUT`、`QUERY_FILE_LAYOUT_INPUT`、`FILE_LAYOUT_ENTRY`、`FILE_LAYOUT_NAME_ENTRY`、`STREAM_LAYOUT_ENTRY`、`FSCTL_GET_NTFS_VOLUME_DATA`、`FSCTL_ENUM_USN_DATA`、`USN_RECORD_V2`、`GetVolumeInformation`、ACLs and the device stack）、flatcap.github.io の NTFS の資料、diskanalyzer.com（WizTree の about / faq）、voidtools.com（Everything の faq）、dev.to の MFT 検索の実装記事、OSR のスレッド
- **Findings**:

| 方式 | 得られるもの | 権限 | 主な難しさ |
|---|---|---|---|
| A. 生の MFT の解析 | すべて（名前・親・サイズ・属性） | 管理者 | update sequence の補正、データランの展開、$MFT の断片化、拡張レコード、予約レコードの除外、8.3 の別名 |
| B. `FSCTL_ENUM_USN_DATA` | 名前・親・属性（**サイズなし**） | 管理者 | サイズを1件ずつ補うと非常に遅い（134万件で約120秒の報告） |
| **C. `FSCTL_QUERY_FILE_LAYOUT`（採用）** | 名前（複数＝ハードリンク、短縮名の印つき）・親のファイル参照番号・属性・ストリームごとの論理サイズと確保サイズ・作成/更新日時 | 管理者（正確な必要条件は要実機確認） | 出力の入れ子の読み解き、Windows 8.1 以降・NTFS 専用、速さは未公表で実測が必要 |

- **C で必要な指定**: `QUERY_FILE_LAYOUT_INCLUDE_NAMES | INCLUDE_STREAMS | INCLUDE_STREAMS_WITH_NO_CLUSTERS_ALLOCATED | INCLUDE_EXTRA_INFO`。**小さなファイル（目録の中に実体が入るもの）を取りこぼさないために「クラスタを持たないストリームも含める」が必須**。範囲（extent）の情報は不要（出力が膨らむだけ）
- **名前の印**: `FILE_LAYOUT_NAME_ENTRY_PRIMARY = 1`、`FILE_LAYOUT_NAME_ENTRY_DOS = 2`。**短縮名（DOS）は除く**
- **サイズの意味**: ストリームの `EndOfFile` が論理サイズ、`AllocationSize` が確保量。`FileInfo.Length`（いまの走査が使う値）は無名のストリームの `EndOfFile` に対応する。ADS（名前付きのストリーム）は `FileInfo.Length` に含まれないので、**いまの走査と合わせるには無名のストリームだけを数える**
- **ルートと親**: ファイル参照番号は下位48ビットが目録の索引、上位16ビットが世代の番号。ルートは索引 5。親の世代の番号が食い違うノードは親が作り替えられた「はぐれ」で、到達できないものは除いて件数を記録する
- **NTFS の判定**: `GetVolumeInformation` のファイルシステム名が `NTFS` であることを一次の条件にし、最初の呼び出しが失敗したら通常の走査へ切り替える（C は ReFS 非対応）
- **BitLocker（解除済み）**: 復号は NTFS より下の層で行われるため、ボリュームの生の読み取りでも平文が読める
- **一貫性**: ロックやスナップショットは不要。読んでいる間に目録が変わるため、新しく作られた/消えた項目の取りこぼしは起こりうる（いまの走査も同じ性質）

### 速さの見込み
- WizTree は「1TB の SSD を5秒未満」「速さは目録を読む入出力だけで決まり、ファイル数にほぼ依らない」と説明している（管理者のときだけ）
- 100万ファイルの目録は約1GB。NVMe の連続読み取り（2〜3GB/s）なら読み取りは1秒未満で、解析を含めて数秒の見込み
- C の速さは未公表。**実測して A に切り替えるかを決める**

### 実機で確かめないと決められないこと（**管理者が必要なため、利用者の作業＝タスク8 が関門**。ここで駄目だった場合に捨てるのは目録を読む部分だけで済むよう、木の組み立てと方式の選択は読み取りの方式に依存しない形にする）
1. ~~C が管理者で動くか。非管理者では期待どおり失敗するか~~ → **非管理者で失敗することは 2026-09-24 に確定**（5 ERROR_ACCESS_DENIED / 1 ERROR_INVALID_FUNCTION）。管理者で動くかはタスク8 のまま
2. C の速さ（100万ファイル規模）。目標（WizTree 以下）に届くか
3. ストリームの印（圧縮・スパースなど）の値と、無名のストリームの見分け方
4. 無名のストリームの `EndOfFile` が `FileInfo.Length` と一致するか（リパースポイント、クラウドの置き換えファイル、目録の中に実体があるファイルで確かめる）
5. 対象がフォルダのときの絞り込みで、ボリューム全体を列挙してから絞るのが十分速いか
6. **`layout-probe` の出力の読み解きが正しいか**。非管理者では1件も返らないため、読み解きの処理は一度も動いていない

### 2026-09-24 の実機の確認（タスク1.2、**管理者でない状態**、Windows 11、NTFS のシステムドライブ）

`Tools/ScanBench` の使い捨ての下位コマンド `layout-probe` で、「開き方 × `CreateFileW` の権限」を総当たりし、
さらに入力の作り方（指定の組み合わせ・入力の大きさ・絞り込みの種類・受け皿の大きさ）を総当たりして切り分けた。
**定数は Windows SDK のヘッダ（`um/winioctl.h`、`um/winnt.h`、`um/fileapi.h`、`um/WinBase.h`）を直接読んで確かめた値を使った。**

#### まず、資料に出回っている値の誤りを正した

`FSCTL_QUERY_FILE_LAYOUT` とその指定の値は、**ヘッダの値が正しい**。作業の指示に載っていた値は誤りだった。

| 名前 | ヘッダ（正） | 出回っていた値（誤） |
|---|---|---|
| `FSCTL_QUERY_FILE_LAYOUT` | **0x00090277**（`CTL_CODE(FILE_DEVICE_FILE_SYSTEM=9, 157, METHOD_NEITHER=3, FILE_ANY_ACCESS=0)`） | 0x00090339 |
| `QUERY_FILE_LAYOUT_RESTART` | **0x00000001** | 0x00000004 |
| `QUERY_FILE_LAYOUT_INCLUDE_NAMES` | **0x00000002** | 0x00000001 |
| `QUERY_FILE_LAYOUT_INCLUDE_STREAMS` | **0x00000004** | 0x00000002 |
| `QUERY_FILE_LAYOUT_INCLUDE_EXTENTS` | 0x00000008 | — |
| `QUERY_FILE_LAYOUT_INCLUDE_EXTRA_INFO` | **0x00000010** | 0x00000008 |
| `QUERY_FILE_LAYOUT_INCLUDE_STREAMS_WITH_NO_CLUSTERS_ALLOCATED` | **0x00000020** | 0x00000010 |

`FILE_LAYOUT_NAME_ENTRY_PRIMARY = 0x00000001` / `FILE_LAYOUT_NAME_ENTRY_DOS = 0x00000002` はヘッダと一致していた。

#### 試した組み合わせと結果（管理者でない状態）

| 開き方 | 権限 | 開く | `FSCTL_IS_VOLUME_MOUNTED` | `FSCTL_GET_NTFS_VOLUME_DATA` | `FSCTL_QUERY_FILE_LAYOUT` |
|---|---|---|---|---|---|
| ボリュームのデバイス | 0（権限なし） | 成功 | 1 ERROR_INVALID_FUNCTION | 1 | 1 |
| ボリュームのデバイス | `SYNCHRONIZE` / `READ_CONTROL` / `FILE_READ_ATTRIBUTES` / `FILE_READ_ATTRIBUTES\|SYNCHRONIZE` | 成功 | 1 | 1 | 1 |
| ボリュームのデバイス | `FILE_READ_DATA` を含むもの、`GENERIC_READ` | **5 ERROR_ACCESS_DENIED** | — | — | — |
| ボリュームのデバイス | `MAXIMUM_ALLOWED` | 成功 | 成功 | 成功 | **5 ERROR_ACCESS_DENIED** |
| ルートのフォルダ（`FILE_FLAG_BACKUP_SEMANTICS`） | 0 から `GENERIC_READ`・`MAXIMUM_ALLOWED` までのすべて | 成功 | 成功 | **成功** | **87 ERROR_INVALID_PARAMETER** |

ルートのフォルダのハンドルに対しては、入力の作り方を10通り
（`RESTART` のみ／`RESTART|NAMES`／`RESTART|NAMES|STREAMS`／既定の組み合わせ／`+EXTENTS`／入力16・32・40バイト／受け皿 64KiB・1MiB・16MiB／参照番号の範囲で絞る）試したが、
**すべて 87 ERROR_INVALID_PARAMETER** で、入力の作り方の問題ではなかった。

#### 分かったこと（要件と設計に直接効く）

1. **`FSCTL_QUERY_FILE_LAYOUT` は管理者が必須である。** 前提は覆らなかった
   - この呼び出しは**ボリュームのハンドル**にしか通らない。ルートのフォルダのハンドルでは入力を何通りに変えても 87 で返る（NTFS が「ボリュームとして開かれたものではない」として断っている）
   - ボリュームのハンドルでは、`MAXIMUM_ALLOWED` で開いて NTFS まで届いた状態でも **5 ERROR_ACCESS_DENIED** で断られる。つまり `FILE_READ_DATA` が要る
   - `\.\C:` を `FILE_READ_DATA` で開くこと自体が管理者でない状態では 5 で失敗する（ボリュームのデバイスの既定の権限）
   - 権限が足りない状態の失敗は **5 ERROR_ACCESS_DENIED** として現れる（要件2.1・2.2 の「管理者でなければ通常の走査へ」の判定に使える。**1 ERROR_INVALID_FUNCTION も同じ扱いにする**）
2. **`FSCTL_GET_NTFS_VOLUME_DATA` は管理者でなくても通る**（ルートのフォルダのハンドルで成功）。ただしこれで得られるのは目録の位置と大きさ・クラスタの大きさだけで、**目録の中身の読み取り（生の解析）にはボリュームの生の読み取り＝管理者が要る**ので、二次の手段（方式A）も管理者が必須であることは変わらない
3. **WizTree が管理者でなくても 3.67〜4.59 秒で終わる理由は、目録をまとめて読む方式ではない。** 管理者でないと目録は読めないため、WizTree は非昇格では通常の列挙で走っている。利用者の実測で「管理者でも速度が変わらなかった」ことと合わせると、**WizTree の速さの主因は目録の方式ではなく、通常の列挙のやり方そのもの**である可能性が高い
4. 管理者でない状態でボリュームのデバイスを 0 や `FILE_READ_ATTRIBUTES` で開くと、ハンドルは得られるが**ファイルシステムまで届かない**（`FSCTL_IS_VOLUME_MOUNTED` すら 1 で返る）。「開けたこと」を「使えること」の判定に使ってはいけない

#### まだ確かめられていないこと（**管理者が必要。タスク8 の関門のまま**）

- 全件の列挙の所要時間と件数（WizTree の 3.67 秒との比較）
- 1件から取れる値（ファイル参照番号・親の参照番号・名前・ハードリンクの別名・短縮名の印・属性・無名のストリームの論理サイズと確保サイズ・更新日時）
- 無名のストリームの論理サイズが `FileInfo.Length` と一致するか（普通・目録の中に実体がある・リパースポイント・圧縮）
- アクセス権の無いフォルダの中身も返るか
- **`layout-probe` の出力の読み解き（構造体の位置と入れ子）そのものが正しいか**。管理者でない状態では1件も返らないため、**読み解きの処理は一度も動いていない**

#### 判断（**親＝人間に要件と設計の見直しをお願いする点**）

- **要件3（昇格の確認）と、管理者を前提にした設計は、そのまま必要である。** 「管理者なしで目録が読めるのではないか」という見込みは、実測で否定された
- いっぽうで、**このフィーチャーの前提そのものに疑いが生まれた**。要件1.2 は「WizTree 以下の所要時間」だが、
  - WizTree は**管理者でない状態で 3.67〜4.59 秒**（同じ機械・同じ対象で、いまのアプリは通常の走査で約 7.1 秒）
  - WizTree は**管理者でも速度が変わらなかった**
  - つまり、このフィーチャー（管理者のときだけ速くなる方式）を完成させても、**利用者が普段使う管理者でない状態の速さは1秒も変わらない**。要件1.2 の比較の相手（非昇格の WizTree）に並ぶには、**通常の列挙の側を速くするしかない**
- したがって、次のいずれかを人間に決めてもらう必要がある
  1. **このフィーチャーを続ける**（管理者のときの上積みとして。ただし非昇格の利用者には効かない）
  2. **通常の列挙を速くする取り組みを先に置く**（`scan-performance` の続き。WizTree が非昇格で出している速さは、目録を使わずに達成されている＝到達できる見込みがある）
  3. 両方を行い、**要件1.2 を「管理者のときは WizTree 以下、管理者でないときは通常の列挙の改善で詰める」と書き分ける**
- あわせて、**要件1.2 の計測の条件に「WizTree を管理者で起動したかどうか」を必ず記録する**ようにしたい（今回、管理者かどうかで差が無いことが分かったため）

#### 管理者で確かめる手順（利用者の作業＝タスク8 の1項目目。`layout-probe` はこの確認のために作ってある）

1. アプリを閉じ、`dotnet build LargeFolderFinder.sln -c Release -warnaserror` でビルドする
2. **管理者のコマンドプロンプト**で、`Tools/ScanBench/bin/Release/net10.0-windows/ScanBench.exe layout-probe C:` を実行する
3. 出力を `.kiro/specs/ntfs-mft-scan/research.md` のこの節に貼る（**ファイル名は出力されない**。件数・時間・型の情報だけが出る）
4. 見るところ
   - 「開き方 × 権限」の表で、**どの組み合わせで `FSCTL_QUERY_FILE_LAYOUT` が成功したか**（`\.\C:` を `FILE_READ_DATA` で開いた行を想定）
   - 「ボリューム全体の列挙」の**所要時間と項目の総数**。利用者の WizTree の 3.67〜4.59 秒と比べる
   - 「無名のストリームの論理サイズと `FileInfo.Length` の突き合わせ」で、**種類ごとに不一致が 0 か**。不一致があれば、その行（サイズの組）をそのまま記録する
   - 「アクセス権の無いフォルダの扱い」で、**件数とサイズの両方が正しく返ったか**
5. 速さが出ない・値が取れない場合は、`--access-only` だけでも記録する（権限の条件の確定になる）
6. 早く終わらせたいときは `--access-only`（権限の確認だけ）、突き合わせの件数を増やしたいときは `--sample 100` を付ける

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|---|---|---|---|---|
| **1. 走査の方式を選ぶ層を足し、MFT の経路を並べる（採用）** | 走査の入口で方式を決め、MFT の経路と通常の経路のどちらかで同じ形の木を作る | 通常の走査に一切触らない。失敗したら切り替えられる | 方式ごとに木を作る処理が2つになる | `scan-performance` が決めた木の規約を両方が守る |
| 2. 通常の走査の中に MFT の経路を差し込む | `DirectoryWalker` の中で分岐 | 部品が増えない | 通常の走査（速度の作り込み済み）に手を入れることになり、回帰の危険が大きい | 採らない |
| 3. 先に目録を読んでから通常の走査で補う | 併用 | — | 二重の作業で遅く、意味も分かりにくい | 採らない |

## Design Decisions

### Decision: 一次の方式は `FSCTL_QUERY_FILE_LAYOUT`（生の MFT の解析は二次）
- **Context**: 要件1（速さと全件）、要件4（集計値の意味）。生の解析は落とし穴が多い
- **Selected**: 文書化された `FSCTL_QUERY_FILE_LAYOUT` を使う。**速さと取れる値の実機の確認は管理者が必要なので、利用者の作業（タスク8 の1項目目）が関門になる**。そこで届かない・必要な値が取れないと分かった場合に限り、生の解析を検討する（判断はこの research.md に記録する）
- **Rationale**: 名前・親・サイズ・ハードリンクの別名・短縮名の印が1回の列挙で取れる。補正や断片化の扱いが不要で、壊れにくい
- **Trade-offs**: Windows 8.1 以降・NTFS 専用（アプリの対象は Windows 10 以降なので問題にならない）。速さは未公表

### Decision: 集計値は「いまの走査と同じ意味」に合わせる
- **Selected**: 無名のストリームの論理サイズ（`EndOfFile`）を数える。ADS（名前付きのストリーム）は数えない。物理サイズ換算はいまと同じ式（クラスタの大きさへの切り上げ）で行う
- **Rationale**: 要件4.4・4.5（同じファイルに同じ大きさ、換算も同じ）。`FileInfo.Length` は無名のストリームの論理サイズに対応する
- **Trade-offs**: ADS を持つファイルは、ディスクの実使用量より小さく出る（いまの走査と同じ性質）

### Decision: 数える対象の違いは「アクセス権の無い場所」と「はぐれ」だけにする
- **Selected**: 予約レコード（目録そのものなど）と短縮名は除く。ハードリンクは名前ごとに数える。到達できない「はぐれ」は結果に含めず件数を記録する
- **Rationale**: 要件4.1・4.2・4.6。通常の走査との差を説明できる形に限定する

### Decision: 管理者の扱い（2026-09-24 の利用者の判断）
- **Selected**: マニフェストで管理者を要求しない。走査のたびに「管理者で開き直すか」を尋ねる。**管理者になれない利用者には尋ねない**。設定で「起動時に管理者で開く」を選べる（既定は無効）。尋ねるのをやめる設定も持つ
- **Rationale**: 管理者権限を持たないアカウントでも起動できるようにする（要件3.8）。将来のドラッグ＆ドロップを妨げない
- **Follow-up**: 昇格して開き直すときに走査の対象を引数で渡し、開き直した直後にその走査を始める（いまの「管理者として開き直す」メニューは対象を引き継がない）

### Decision: MFT の走査の検証は、昇格していない環境では飛ばす
- **Context**: 自動の試験（`dotnet test`）と検証ツールは管理者ではない状態で走る。MFT の経路は管理者でしか動かない
- **Selected**: 自己検証の項目は「管理者でなければ、その理由を残して飛ばす（成功にはしない）」形にする。管理者での確認は、利用者が管理者のコマンドプロンプトから検証ツールと計測の道具を走らせる手順を用意する
- **あわせて、木の組み立て（`MftScanner.Build`）は目録の並びを受け取る形にし、`VolumeFileEntry`・`MftScanResult`・`MftScanner` を public にする**（`ScanParallelism`・`ScanTuning` と同じ扱い。`InternalsVisibleTo` は使わない方針のため、検証ツールが反射に頼らず呼べるようにする）。これで集計の意味・ハードリンク・はぐれ・絞り込み・換算・進み具合・取り消しは管理者なしで自動に確かめられる
- 既存の自己検証のうち「アクセス拒否の付与・解除が管理者を必要としないこと」を確かめる項目は、管理者では必ず落ちるので**管理者のときは飛ばす**
- **Rationale**: 開発の自動の検証を壊さず、確かめたことの記録を残せる

### Decision: 目録の読み取りを試す入口は使い捨てにする（2026-09-24、タスク1.2）
- **Context**: 管理者が要るかどうかを早く確かめる必要があったが、本体の部品（5.1）を作るのは 3.2・4.1 の後になる
- **Selected**: `Tools/ScanBench` の下位コマンド `layout-probe`（`Tools/ScanBench/LayoutProbe.cs`）として、本体には入れずに作った
- **約束**: この入口は**早い段階の確認のための使い捨て**である。**5.1 で `Services/VolumeLayoutReader.cs` と `Helpers/Win32Volume.cs` ができ、6.1 で計測の道具に方式の指定が入ったら、`Tools/ScanBench/LayoutProbe.cs` と `Program.cs` の下位コマンドの分岐を取り除く。** 同じ OS の直接呼び出しを本体と道具の両方に残さない
- **守っていること**: ボリュームは読み取りだけで開く（書き込みの呼び出しを使わない）。列挙で得た名前は経路の組み立てと `FileInfo.Length` との突き合わせにだけ使い、出力にも記録にも出さない。一時ファイルは「アクセス権の無いフォルダ」の確認のためだけに作り、終わりに必ず消す

## Risks & Mitigations
- **このフィーチャーを完成させても、管理者でない利用者の速さは変わらない（2026-09-24 に判明）** → WizTree は非昇格で 3.67〜4.59 秒、いまのアプリは 7.1 秒。目録の方式は管理者のときしか効かないため、要件1.2 の比較の相手に非昇格で並ぶには通常の列挙を速くするしかない。**要件と設計の見直しを人間に仰ぐ**（上記「判断」）
- **`FSCTL_QUERY_FILE_LAYOUT` が期待どおり動かない/遅い** → 確認には管理者が必要で、開発の作業の中では確かめられない。利用者の作業（タスク8）の1項目目を関門とし、結果で「この方式で進める／生の解析に切り替える／フィーチャーを見直す」を判断して research.md に記録する。関門より前に作るものは、駄目だったときに捨てる範囲が目録を読む部分だけで済むように分けておく
- **集計値がいまの走査とずれる** → 同じ対象を両方の方式で走査して突き合わせる検証を用意する。差が出るのは「アクセス権の無い場所」だけであることを確かめる
- **管理者で動くコードの危険** → ボリュームは読み取りだけで開き、書き込みの呼び出しを使わない。得た名前や構成は結果とログ以外に残さない
- **昇格のダイアログが煩わしい** → 尋ねない設定と、起動時に昇格する設定を用意する。管理者になれない利用者には尋ねない
- **走査の方式が増えることによる混乱** → 完了時にどちらの方式で走査したかを示す。ハードリンクにより合計がディスクの実使用量より大きく出ることを記載する

## References
- [FSCTL_QUERY_FILE_LAYOUT](https://learn.microsoft.com/en-us/windows-hardware/drivers/ifs/fsctl-query-file-layout)
- [QUERY_FILE_LAYOUT_INPUT](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/ns-ntifs-_query_file_layout_input)
- [FILE_LAYOUT_ENTRY](https://microsoft.github.io/windows-docs-rs/doc/windows/Win32/System/Ioctl/struct.FILE_LAYOUT_ENTRY.html) / [FILE_LAYOUT_NAME_ENTRY](https://microsoft.github.io/windows-docs-rs/doc/windows/Win32/System/Ioctl/struct.FILE_LAYOUT_NAME_ENTRY.html) / [STREAM_LAYOUT_ENTRY](https://microsoft.github.io/windows-docs-rs/doc/windows/Win32/System/Ioctl/struct.STREAM_LAYOUT_ENTRY.html)
- [FSCTL_GET_NTFS_VOLUME_DATA](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-fsctl_get_ntfs_volume_data) / [NTFS_VOLUME_DATA_BUFFER](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ns-winioctl-ntfs_volume_data_buffer)
- [FSCTL_ENUM_USN_DATA](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-fsctl_enum_usn_data) / [USN_RECORD_V2](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ns-winioctl-usn_record_v2)
- [ACLs and the device stack](https://learn.microsoft.com/en-us/previous-versions/windows/drivers/storage/acls-and-the-device-stack)
- [GetVolumeInformation](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getvolumeinformationw)
- [NTFS の資料（ファイルレコード・属性・名前空間・補正）](https://flatcap.github.io/linux-ntfs/ntfs/concepts/file_record.html)
- [WizTree について](https://diskanalyzer.com/about) / [WizTree の FAQ](https://diskanalyzer.com/faq)
- [Everything の FAQ](https://www.voidtools.com/faq/)
- [MFT を直接読む実装の記事（USN 方式との比較の実測）](https://dev.to/jearry/why-i-replaced-everything-with-a-homegrown-mft-search-engine-memory-hundreds-of-mb-a-few-mb-p87)
- 参考にできる実装（取り込まず方式の確認のみ）: [NtfsLib（MIT）](https://github.com/LordMike/NtfsLib)、[DiscUtils（MIT）](https://github.com/DiscUtils/DiscUtils)
