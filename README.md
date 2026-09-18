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

- **Fast Multi-thread Scan**: Parallel processing allows for rapid scanning of drives containing a large number of files.
  - PC Example: **Approx. 400GB (approx. 1.17M files) on PC → 5 ~ 13 seconds**
  - NAS Example 1: **Approx. 1TB (approx. 70K files) on NAS → 23 seconds**
  - NAS Example 2: **Approx. 20TB (approx. 1.4M files) on NAS → Approx. 18 ~ 30 minutes**
- **Server Support**: Supports scanning via network (NAS, etc.).
- **Tabs and History Saving**: Scan results are saved locally and can be viewed in multiple tab windows.
  - Once scanned, you can easily modify the display results anytime—such as filtering or sorting—to make them easier to view.
- **Advanced Customization**:
  - Enable/disable parallel processing, sector size consideration, skip pre-scan counting, etc.
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
Win32 API (kernel32.dll) を使用しC++並に爆速で動くことを目指して作成されています。

## 🔍 スキャン結果の表示例

指定したサイズ（例: 10GB）以上のフォルダーのみを抽出し、Tree形式で表示します。

(一度スキャンすれば、再スキャンなしで表示条件は変更可能です。)

![アプリケーションのスクリーンショット（日本語）](README.md_Resource/AppScreenshot_ja.png)

## ✨ 特徴

- **高速マルチスレッドスキャン**: 並列処理により、大量のファイルを含むドライブも迅速にスキャンします。
  - PC実績例  : **約400GB (約117万ファイル) PC上のデータ   → 5 ~ 13秒**
  - NAS実績例1: **約1TB   (約7万ファイル)   NAS上のデータ  → 23秒**
  - NAS実績例2: **約20TB  (約140万ファイル) NAS上のデータ  → 約18 ~ 30分**
- **サーバー対応**: ネットワーク経由（NAS等）のスキャンも可能です。
- **履歴の保存**: スキャン結果はローカル内に保存され、複数のタブWindowで閲覧できます。
  - 一度スキャンすれば、フィルタリングやソートなど表示結果をいつでも見やすく変更できます。
- **高度なカスタマイズ**:
  - 並列処理・セクタサイズの配慮・事前スキャンのスキップなど有効化/無効化
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
