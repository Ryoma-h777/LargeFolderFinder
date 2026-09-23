# Large Folder Finder

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

[**🇯🇵 日本語 (Japanese Version) is here**](#large-folder-finder-japanese)

This desktop application quickly searches folders on Windows and visually displays their structure and size in a tree format.
It excels at exploring network drives like NAS, helping you quickly pinpoint causes of disk space pressure.

## 🔍 Scan Result Example

Extracts and lists only folders larger than the specified size (e.g., 10 GB) in a Tree format.

(Once scanned, display settings can be changed without rescan.)
![Application Screenshot (English)](README.md_Resource/AppScreenshot_en.png)

## ✨ Features

- **Fast Multi-thread Scan**: A fixed number of workers read folders at the same time, so drives containing a large number of files are scanned quickly.
  - PC Example: **Approx. 400GB (approx. 1.17M files) on PC → 5 ~ 13 seconds**
  - NAS Example 1: **Approx. 1TB (approx. 70K files) on NAS → 23 seconds**
  - NAS Example 2: **Approx. 20TB (approx. 1.4M files) on NAS → Approx. 18 ~ 30 minutes**
  - Measured in 2026-09 with the automatic parallelism: **a whole system drive, approx. 725GiB (approx. 1.78M files) → approx. 7.1 seconds** (approx. 11.6 seconds before this update), and **the OS folder, approx. 210K files → approx. 2.4 seconds** (approx. 4.0 seconds before).
    - Environment: Intel Core i7-12700H (20 logical processors) / NVMe SSD (local) / Windows 11, run without administrator rights, median of the second and later runs. This was measured on a **different PC and different targets** from the examples above, so it is listed alongside them instead of replacing them.
- **Server Support**: Supports scanning via network (NAS, etc.).
- **Tabs and History Saving**: Scan results are saved locally and can be viewed in multiple tab windows.
  - Once scanned, you can easily modify the display results anytime—such as filtering or sorting—to make them easier to view.
- **Advanced Customization**:
  - Enable/disable parallel processing and set the number of parallel workers (see "Scan Speed Settings" below), sector size consideration, skip pre-scan counting, etc.
  - Output format & copy results to clipboard
    - Show/hide files
    - Filter function (wildcard or regular expression)
    - Folder collapse feature (also reflected in clipboard output)
    - Adjust display units (KB, MB, GB, TB)
    - Font size adjustment

- **Multilingual Support**: Automatically detects OS language settings (Supports 13 languages including Japanese, English, Chinese, etc.).

## 🚀 How to Use

1. **Download**: Download one of the zip files from the latest release on the [Releases](https://github.com/Ryoma-h777/LargeFolderFinder/releases) page (if unsure, choose `LargeFolderFinder.zip`; see [Which Download to Choose](#-which-download-to-choose)).
2. **Unzip**: Extract the whole zip into any folder. Keep `Config.txt` and the `Resources` folder next to `LargeFolderFinder.exe`.
3. **Run**: Launch `LargeFolderFinder.exe`. No installation is needed.
4. **Configure**: Select the path to scan and enter the minimum size to extract (e.g., 1 GB).
5. **Scan**: Click the ▶ button (Scan button).
6. **Utilize**: Copy the results using the button and use them for disk space management.

※For details, please refer to the Resources/Readme/Readme_{language}.txt file for each language.

## ⚙️ Scan Speed Settings (`Config.txt`)

`Config.txt` sits next to `LargeFolderFinder.exe` and can be opened with Notepad. Two items decide how many folders are read at the same time.

| Item | Values | What it does |
|---|---|---|
| `UseParallelScan` | `true` (default) / `false` | `false` reads folders one at a time. **This wins over `ScanThreads`**: while it is `false`, the scan is sequential no matter what `ScanThreads` says. |
| `ScanThreads` | `0` (default) / `1` to `64` | `0` means automatic. `1` or more fixes the number of workers. A value above `64` is capped to `64`. |

- **Leave `ScanThreads` at `0` (automatic) unless you have a reason to change it.** Automatic uses the number of logical processors clamped to **4 – 8** for a local drive, and **16** for a network target (a `\\server\share` path, or a mapped network drive).
- **A larger value is not always faster.** On a local SSD, going above 8 did not shorten the scan in our measurements, and on a laptop that cannot hold a high CPU clock for long, a large value made repeated scans nearly twice as slow. On a NAS, where most of the time is spent waiting for the network, a larger value can help — try `16`, `24` or `32` on your own share and keep the fastest.
- When you compare settings, scan the same folder two or more times in a row and compare the **second and later** runs. The first run is slower with any setting, because Windows has not cached the folder yet.
- `UseParallelScan: false` (sequential) is kept on purpose: for an HDD-based NAS where many requests at once cause seek thrashing, and for checking whether a problem is caused by parallel scanning.
- **If you go back to an older version of the app, also restore the `Config.txt` that came with that version.** An older version cannot read the `ScanThreads` line, falls back to the default settings, and shows a warning.

## 📦 Which Download to Choose

Each release provides two zip files. The contents (settings file, language files, Readme, licenses) and the features are the same; only whether the .NET runtime is included differs.

| | `LargeFolderFinder.zip` (Standard, recommended) | `LargeFolderFinder-FrameworkDependent.zip` (Lightweight) |
|---|---|---|
| .NET runtime | Included in the exe (no installation needed) | Not included (.NET 10 Desktop Runtime (x64) must be installed) |
| Download size (zip) | Approx. 59 MB | Approx. 0.5 MB |
| Size of `LargeFolderFinder.exe` | Approx. 140 MB | Approx. 1 MB |

- **If unsure, choose `LargeFolderFinder.zip`.** It runs as-is on a PC without the .NET runtime.
- Choose `LargeFolderFinder-FrameworkDependent.zip` if the .NET 10 Desktop Runtime (x64) is already installed on your PC, or if you want a smaller download.
  - The runtime can be downloaded from Microsoft: [Download .NET 10.0](https://dotnet.microsoft.com/download/dotnet/10.0) → ".NET Desktop Runtime 10.0.x" → Windows "x64" installer.
  - If the runtime is not installed, the app will not start and a message asking you to install .NET is shown. Install the runtime above and start the app again.

## 💻 System Requirements

- **OS**: Windows 10 / 11 (64-bit, x64)
- **Runtime**:
  - `LargeFolderFinder.zip`: None required (the .NET runtime is included)
  - `LargeFolderFinder-FrameworkDependent.zip`: .NET 10 Desktop Runtime (x64)

## 📄 License

This project is released under the [MIT License](Resources/License/LICENSE.txt). Anyone may use it freely and at no cost, including for commercial purposes.

If you are unable to display the MIT License attribution, you may use it under the following conditions:
※ You do not need to wait for my reply to start using it; you may begin immediately.
- Support via GitHub Sponsors (https://github.com/sponsors/Ryoma-h777) exempts you from the MIT license attribution requirement.

- Post on social media (e.g., X) stating you are using the tool, including the “Tool Name” and “Creator” (Ryoma Henzan, Cat & Chocolate Laboratory), and notify the developer.

- If you do not have a social media account, you may obtain permission to post on the developer's social media, including your company name or personal name.

- If you encounter any other issues, feel free to use the app. We'll be flexible in our response.

---

<div id="japanese-version"></div>

# Large Folder Finder (Japanese)

Windows上でフォルダーを高速に検索し、構造とサイズをTree状に視覚化するデスクトップアプリです。
特にNASなどのネットワークドライブでの探索で活躍しており、ディスク容量の圧迫原因を素早く特定するのに役立ちます。
決まった数のワーカーが同時にフォルダーを読む方式で、C++並に爆速で動くことを目指して作成されています。

## 🔍 スキャン結果の表示例

指定したサイズ（例: 10GB）以上のフォルダーのみを抽出し、Tree形式で表示します。

(一度スキャンすれば、再スキャンなしで表示条件は変更可能です。)

![アプリケーションのスクリーンショット（日本語）](README.md_Resource/AppScreenshot_ja.png)

## ✨ 特徴

- **高速マルチスレッドスキャン**: 決まった数のワーカーが同時にフォルダーを読むため、大量のファイルを含むドライブも迅速にスキャンします。
  - PC実績例  : **約400GB (約117万ファイル) PC上のデータ   → 5 ~ 13秒**
  - NAS実績例1: **約1TB   (約7万ファイル)   NAS上のデータ  → 23秒**
  - NAS実績例2: **約20TB  (約140万ファイル) NAS上のデータ  → 約18 ~ 30分**
  - 2026-09 の計測（並列度は自動）: **システムドライブ全体 約725GiB (約178万ファイル) → 約7.1秒**（この更新の前は約11.6秒）、**OS本体のフォルダ (約21万ファイル) → 約2.4秒**（前は約4.0秒）
    - 計測した環境: Intel Core i7-12700H（20論理プロセッサ）/ NVMe SSD（ローカル）/ Windows 11。管理者ではない状態で、2回目以降の中央値。**上の実績例とはPCも対象も異なる**ため、置き換えずに併記しています
- **サーバー対応**: ネットワーク経由（NAS等）のスキャンも可能です。
- **履歴の保存**: スキャン結果はローカル内に保存され、複数のタブWindowで閲覧できます。
  - 一度スキャンすれば、フィルタリングやソートなど表示結果をいつでも見やすく変更できます。
- **高度なカスタマイズ**:
  - 並列処理の有効化/無効化と並列度の指定（下の「スキャンの速さの設定」を参照）、セクタサイズの配慮、事前スキャンのスキップなど
  - 出力フォーマット & 結果のクリップボードへのコピー
    - ファイルの表示/非表示
    - フォルダの折りたたみ機能(クリップボード出力にも反映)
    - フィルタ機能(ワイルドカードor正規表現)
    - 表示単位（KB, MB, GB, TB）の調整
    - フォントサイズの調整
- **多言語対応**: OSの言語設定を自動認識（日本語・英語・中国語など、全13言語）。

## 🚀 使い方

1. **ダウンロード**: [Releases](https://github.com/Ryoma-h777/LargeFolderFinder/releases) ページの最新のリリースから、zip を1つダウンロードします（迷ったら `LargeFolderFinder.zip`。[配布物の選び方](#-配布物の選び方)を参照）。
2. **解凍**: zip を丸ごと好きなフォルダーに解凍します。`Config.txt` と `Resources` フォルダーは `LargeFolderFinder.exe` の隣に置いたままにしてください。
3. **実行**: `LargeFolderFinder.exe` を起動します。インストールは不要です。
4. **設定**: スキャンしたいパスを選択し、抽出する最小サイズ（例: 1GB）を入力します。
5. **スキャン**: ▶ボタン(スキャンボタン)をクリックします。
6. **活用**: 結果をコピーボタンで取得し、容量整理の資料として利用できます。

※詳しくは各言語の Resources/Readme/Readme_{language}.txt に記載されています。

## ⚙️ スキャンの速さの設定（`Config.txt`）

`Config.txt` は `LargeFolderFinder.exe` の隣にあり、メモ帳で開いて編集できます。同時にいくつのフォルダーを読むかは、次の2つで決まります。

| 項目 | 設定できる値 | 意味 |
|---|---|---|
| `UseParallelScan` | `true`（既定） / `false` | `false` にすると1つずつ順に読みます。**こちらが優先**で、`false` の間は `ScanThreads` の値に関わらず逐次でスキャンします。 |
| `ScanThreads` | `0`（既定） / `1` 〜 `64` | `0` は自動。`1` 以上にするとワーカーの数をその値に固定します。`64` を超える値は `64` に丸めます。 |

- **理由がなければ `ScanThreads` は `0`（自動）のままで使ってください。** 自動のときは、ローカルのドライブでは論理プロセッサ数を **4〜8** に丸めた値、ネットワーク（`\\サーバー\共有` のパスやネットワークドライブ）では **16** を使います。
- **大きくすれば速くなるとは限りません。** ローカルのSSDでは8を超えて増やしても計測では速くならず、CPUの高いクロックを長く保てないノートPCでは、スキャンを続けるうちに大きい値ほど2倍近く遅くなりました。一方、待ち時間が多いNASでは大きいほうが効く場合があります。お使いの共有で `16`・`24`・`32` などを試し、速かった値を使ってください。
- 設定を比べるときは、同じフォルダーを続けて2回以上スキャンし、**2回目以降**の時間で比べてください。1回目はWindowsがまだ内容を覚えていないため、どの設定でも遅くなります。
- `UseParallelScan: false`（逐次）は意図して残しています。HDDのNASなど、同時に読みに行くとかえって遅くなる環境や、不具合が並列処理のせいかどうかを切り分けたいときに使えます。
- **古い版のアプリに戻すときは、`Config.txt` もその版に付属のものに戻してください。** 古い版は `ScanThreads` の行を読めず、設定が既定に戻って警告が表示されます。

## 📦 配布物の選び方

リリースごとに2つの zip を用意しています。中身（設定ファイル・言語ファイル・Readme・ライセンス）と機能は同じで、.NET のランタイムを含むかどうかだけが違います。

| | `LargeFolderFinder.zip`（標準・おすすめ） | `LargeFolderFinder-FrameworkDependent.zip`（軽量版） |
|---|---|---|
| .NET のランタイム | exe に同梱（インストール不要） | 同梱しない（.NET 10 Desktop Runtime (x64) のインストールが必要） |
| ダウンロードの大きさ（zip） | 約59MB | 約0.5MB |
| `LargeFolderFinder.exe` の大きさ | 約140MB | 約1MB |

- **迷ったら `LargeFolderFinder.zip` を選んでください。** .NET のランタイムが入っていない PC でも、そのまま起動できます。
- すでに .NET 10 Desktop Runtime (x64) を入れている PC や、ダウンロードを小さくしたい場合は `LargeFolderFinder-FrameworkDependent.zip` を選べます。
  - ランタイムは Microsoft の [.NET 10.0 のダウンロード](https://dotnet.microsoft.com/download/dotnet/10.0) ページの「.NET Desktop Runtime 10.0.x」から、Windows の「x64」のインストーラーを入手できます。
  - ランタイムが入っていないとアプリは起動せず、.NET のインストールを求めるメッセージが表示されます。上記のランタイムを入れてから、もう一度起動してください。

## 💻 システム要件

- **OS**: Windows 10 / 11（64ビット、x64）
- **ランタイム**:
  - `LargeFolderFinder.zip`: 不要（.NET のランタイムを同梱）
  - `LargeFolderFinder-FrameworkDependent.zip`: .NET 10 Desktop Runtime (x64)

## 📄 ライセンス

このプロジェクトは [MIT ライセンス](Resources/License/LICENSE.txt) の下で公開されています。商用利用を含め、どなたでも無料で自由にご利用いただけます。

MITライセンスの表記ができない場合、以下の対応でもご利用可能です。
※利用開始は、私からの返事を待つ必要はなく、すぐにご利用を開始して構いません。
- [GitHub Sponsors で支援する](https://github.com/sponsors/Ryoma-h777)にてMITライセンス表記の免除を行う。

- X等SNSで「ツール名」と「作成者」(Ryoma Henzan, Cat & Chocolate Laboratory)を記載して利用していることを投稿していただき、開発者へその旨を伝える。

-  SNSアカウントをお持ちでない際は、「開発者のSNSで利用していることを御社名または個人名を含んで投稿してもよい」と許可していただく。

- その他不都合ございましたら、気軽にご利用くださって大丈夫です。柔軟に対応いたします。

## ☕ 支援・スポンサーシップのお願い

本アプリが業務の効率化に役立ち、開発を継続的にサポートいただける場合は、ぜひご支援をお願いいたします。

特に、**企業様（目安として年商10億円以上など）**で本アプリをご活用いただいている場合は、今後のメンテナンスや新機能追加のために寄付（GitHub Sponsors 等）をご検討いただけますと幸いです。

- [GitHub Sponsors で支援する](https://github.com/sponsors/Ryoma-h777)
巡り巡っていろんな人に支えられた結果リリースできております。
支援があってもなくても、少しでもお役に立てたのなら感謝します。
「使っています」と一報いただけるだけで、私がすごく笑顔になり更に励みます。
これからもよろしくお願いいたします。

## 👤 作者

**Ryoma Henzan / Cat & Chocolate Laboratory**
