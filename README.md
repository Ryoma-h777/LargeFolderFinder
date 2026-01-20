# Large Folder Finder

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

[**🇯🇵 日本語 (Japanese Version) is here**](#-large-folder-finder-japanese-version)

A desktop application for Windows that rapidly searches for folders and visualizes their structure and size.
It excels particularly in exploring network drives like NAS, helping you quickly identify the causes of disk space usage.

## 🔍 Scan Result Example

Extracts and lists only folders larger than the specified size (e.g., 10 GB) in a Tree format.

```text
PATH: C:\   MinSize: 10 GB
Finished [Time: 6s]

C:\                                                   378 GB
┣$Recycle.Bin                                          39 GB
┃ ┗S-1-5-21-3796979980-2337565616-3929222400-1001      39 GB
┣Users                                                123 GB
┃ ┗User                                    123 GB
┃   ┣AppData                                           87 GB
┃   ┃ ┗Local                                           81 GB
┃   ┃   ┣Google                                        14 GB
┃   ┃   ┃ ┗Chrome                                      13 GB
┃   ┃   ┃   ┗User Data                                 13 GB
┃   ┃   ┃     ┗Default                                 11 GB
┃   ┃   ┣Temp                                          20 GB
┃   ┃   ┗wsl                                           23 GB
┃   ┃     ┗{b85b4030-fb7f-40f0-8e56-33dc627f70ae}      23 GB
┃   ┗Downloads                                         18 GB
┗Windows                                               45 GB
  ┗WinSxS                                              20 GB
```

## ✨ Features

- **Fast Multi-thread Scan**: Parallel processing allows for rapid scanning of drives containing a large number of files.
- **Server Support**: Supports scanning via network (NAS).
  - Achievement: **Scanned approx. 1TB of NAS data in 15 seconds.**
  - Achievement: **Scanned approx. 20TB of NAS data in 30 minutes.** (Depends on file count)
- **Advanced Customization**:
  - Toggle parallel processing, sector size consideration, skip pre-scan counting.
  - Adjust output format (tab/space) and units (KB, MB, GB, TB).
- **Multilingual Support**: Automatically detects OS language settings (Supports 13 languages including English, Japanese, Chinese, etc.).

## 🚀 How to Use

1. **Download**: Download and unzip the latest `LargeFolderFinder.zip` from the [Releases](https://github.com/Ryoma-h777/LargeFolderFinder/releases) page.
2. **Run**: Launch `LargeFolderFinder.exe`.
3. **Configure**: Select the path to scan and enter the minimum size to extract (e.g., 1 GB).
4. **Scan**: Click "Scan".
5. **Utilize**: Copy the results using the button and use them for disk space management.

## 💻 System Requirements

- **OS**: Windows 10 / 11
- **Runtime**: .NET Framework 4.8 (Standard on Windows, usually no installation required)

## 📄 License

This project is released under the [MIT License](LICENSE). Anyone can use it freely for free, including for commercial purposes.

---

<div id="japanese-version"></div>

# Large Folder Finder (日本語)

Windows上でフォルダーを高速に検索し、構造とサイズを視覚化するデスクトップアプリです。
特にNASなどのネットワークドライブでの探索で活躍しており、ディスク容量の圧迫原因を素早く特定するのに役立ちます。

## 🔍 スキャン結果の表示例

指定したサイズ（例: 10GB）以上のフォルダーのみを抽出し、Tree形式で表示します。

```text
PATH: C:\   MinSize: 10 GB
完了 [処理時間: 6秒]

C:\                                                   378 GB
┣$Recycle.Bin                                          39 GB
┃ ┗S-1-5-21-3796979980-2337565616-3929222400-1001      39 GB
┣Users                                                123 GB
┃ ┗User                                    123 GB
┃   ┣AppData                                           87 GB
┃   ┃ ┗Local                                           81 GB
┃   ┃   ┣Google                                        14 GB
┃   ┃   ┃ ┗Chrome                                      13 GB
┃   ┃   ┃   ┗User Data                                 13 GB
┃   ┃   ┃     ┗Default                                 11 GB
┃   ┃   ┣Temp                                          20 GB
┃   ┃   ┗wsl                                           23 GB
┃   ┃     ┗{b85b4030-fb7f-40f0-8e56-33dc627f70ae}      23 GB
┃   ┗Downloads                                         18 GB
┗Windows                                               45 GB
  ┗WinSxS                                              20 GB
```

## ✨ 特徴

- **高速マルチスレッドスキャン**: 並列処理により、大量のファイルを含むドライブも迅速にスキャンします。
- **サーバー対応**: ネットワーク経由（NAS等）のスキャンも可能です。
  - 実績例1: **約1TBのNAS上のデータを15秒でスキャン**
  - 実績例2: **20TB規模のNAS上のデータを30分でスキャン**（※ファイル数による）
- **高度なカスタマイズ**:
  - 並列処理・セクタサイズの配慮・事前スキャンのスキップなど有効化/無効化
  - 出力フォーマット（タブ、スペース）、表示単位（KB, MB, GB, TB）の調整
- **多言語対応**: OSの言語設定を自動認識（日本語・英語・中国語など、全13言語）。

## 🚀 使い方

1. **ダウンロード**: [Releases](https://github.com/Ryoma-h777/LargeFolderFinder/releases) ページから最新の `LargeFolderFinder.zip` をダウンロード・解凍します。
2. **実行**: `LargeFolderFinder.exe` を起動します。
3. **設定**: スキャンしたいパスを選択し、抽出する最小サイズ（例: 1GB）を入力します。
4. **スキャン**: 「スキャン開始」をクリックします。
5. **活用**: 結果をコピーボタンで取得し、容量整理の資料として利用できます。

## 💻 システム要件

- **OS**: Windows 10 / 11
- **ランタイム**: .NET Framework 4.8 (Windowsに標準搭載されているため、通常はインストール不要です)

## 📄 ライセンス

このプロジェクトは [MIT ライセンス](LICENSE) の下で公開されています。商用利用を含め、どなたでも無料で自由にご利用いただけます。

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
