Large Folder Finder
====================
一個快速提取並列出大於指定大小的資料夾的工具。


■ 使用方法
--------------------
1. 選擇您要調查的資料夾。
2. 指定您要提取的最小大小。
3. 點擊「Scan」按鈕開始搜尋。
4. 結果以文字格式顯示。
5. 點擊右上角的複製按鈕（📄 圖示）將結果複製到剪貼簿。


■ 進階設定 (Config.txt)
--------------------
透過編輯應用程式目錄中的「Config.txt」，您可以設定詳細的行為。
點擊 UI 上的「⚙」按鈕可立即使用記事本等文字編輯器將其開啟。
設定必須遵循 YAML 格式。如果您想新增自己的註釋，請在行首加上 #。

    ▽ 可設定項目：(預設值)
    UseParallelScan: true
        類型：bool (true/false)
        說明：啟用平行掃描。
        背景 (true)：對 NAS（網路儲存）等有效。由於本地 SSD 速度很快，並行化的開銷可能會更大。

    SkipFolderCount: false
        類型：bool (true/false)
        說明：是否跳過進度顯示的預計數並立即開始掃描。
        如果設為 true，則由於無法得知資料夾總數，進度百分比將無法顯示。

    MaxDepthForCount: 3
        類型：int (自然數)
        說明：為了確定進度百分比，在掃描前執行資料夾總數計數的最大目錄深度。
        較大的深度值可能會耗費更多時間，但會提高進度顯示的準確度。
        範例 (3)：NAS: 3~6，本地 PC: 7~

    UsePhysicalSize: true
        類型：bool (true/false)
        說明：是否考慮磁簇大小來計算「磁碟上的實際佔用大小」。
        範例 (true)：通常推薦設為 true。結果將更接近 Windows 屬性顯示的值。如果設為 false，則按檔案實際大小計算。
        在調整此設定前，建議以管理員權限執行程式，以便準確地將系統檔案納入計算。


■ 如何新增語言檔案
--------------------
本工具支援多種語言，您可以新增新語言。
1. 打開與執行檔 (.exe) 位於同一層級的「Languages」資料夾。
2. 複製現有檔案（如「en.yaml」），並將檔名更改為您要新增的語言的文化代碼（例如，法語為「fr.yaml」）。
   * 請參考 Microsoft 文件查看文化代碼列表：
   https://learn.microsoft.com/zh-tw/windows-hardware/manufacture/desktop/available-language-packs-for-windows?view=windows-11
3. 編輯 YAML 檔案中的文字（請以 UTF-8 格式儲存）。
4. 重啟應用程式，新語言將出現在「Language」選單中。
* 如有必要，請參考其他檔案建立並新增 Readme_<code>.txt。


■ 完全解除安裝（刪除設定和日誌）
--------------------
如需完全刪除本工具的設定和執行日誌，請手動刪除以下資料夾：
%LOCALAPPDATA%\Cat & Chocolate Laboratory\LargeFolderFinder
（您可以將上述路徑貼到檔案總管的網址列中直接打開）


■ Copyright
--------------------
Copyright (C) 2026 Ryoma Henzan / Cat & Chocolate Laboratory
